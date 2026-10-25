using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CinematicCam.Interop;
using static CinematicCam.Interop.SimConnectNative;

namespace CinematicCam.Sim;

public enum ConnectionState { Disconnected, Connecting, Connected }

/// <summary>
/// SimConnect bağlantısını tek bir arka plan iş parçacığında yönetir. Tüm SimConnect
/// çağrıları bu iş parçacığında yapılır; diğer iş parçacıkları <see cref="Post"/> ile iş gönderir.
/// </summary>
public sealed class SimConnectClient : IDisposable
{
    private const string ClientName = "CinematicCam";
    private const uint DefAircraft = 1;
    private const uint ReqAircraft = 1;
    private const uint ReqAircraftLoaded = 2;
    private const uint EvtAircraftLoaded = 1;

    private static readonly (string Name, string Units)[] AircraftFields =
    {
        ("PLANE LATITUDE", "degrees"),
        ("PLANE LONGITUDE", "degrees"),
        ("PLANE ALTITUDE", "meters"),
        ("PLANE ALT ABOVE GROUND", "meters"),
        ("PLANE PITCH DEGREES", "degrees"),
        ("PLANE BANK DEGREES", "degrees"),
        ("PLANE HEADING DEGREES TRUE", "degrees"),
        ("VELOCITY BODY X", "meters per second"),
        ("VELOCITY BODY Y", "meters per second"),
        ("VELOCITY BODY Z", "meters per second"),
        ("VELOCITY WORLD Y", "meters per second"),
        ("ACCELERATION BODY X", "meters per second squared"),
        ("ACCELERATION BODY Y", "meters per second squared"),
        ("ACCELERATION BODY Z", "meters per second squared"),
        ("ROTATION VELOCITY BODY X", "degrees per second"),
        ("ROTATION VELOCITY BODY Y", "degrees per second"),
        ("ROTATION VELOCITY BODY Z", "degrees per second"),
        ("SIM ON GROUND", "bool"),
        ("GENERAL ENG THROTTLE LEVER POSITION:1", "percent"),
        ("GENERAL ENG COMBUSTION:1", "bool"),
        ("WING SPAN", "meters"),
        ("GROUND VELOCITY", "meters per second"),
        ("AIRSPEED TRUE", "knots"),
    };

    private static readonly int DataOffset = Marshal.SizeOf<RecvSimObjectData>();

    private readonly ConcurrentQueue<Action> _work = new();
    private readonly AutoResetEvent _simEvent = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private IntPtr _handle;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public CameraAvailability CameraState { get; private set; } = CameraAvailability.NotAcquired;
    public bool CameraAcquired => CameraState == CameraAvailability.Acquired;

    /// <summary>Her görsel karede, SimConnect iş parçacığında tetiklenir.</summary>
    public event Action<AircraftRaw>? AircraftData;
    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<CameraAvailability>? CameraStateChanged;
    /// <summary>Yüklü uçağın aircraft.cfg yolu (ör. simobjects\airplanes\...\aircraft.cfg).</summary>
    public event Action<string>? AircraftLoaded;
    public event Action<string>? Log;

    public SimConnectClient()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SimConnect", Priority = ThreadPriority.AboveNormal };
    }

    public void Start() => _thread.Start();

    /// <summary>Bir işi SimConnect iş parçacığında çalıştırmak üzere kuyruğa alır.</summary>
    public void Post(Action action)
    {
        _work.Enqueue(action);
        _simEvent.Set();
    }

    public void AcquireCamera() => Post(() =>
    {
        if (_handle == IntPtr.Zero) return;
        Check(SimConnect_CameraAcquire(_handle, ClientName), "CameraAcquire");
        Check(SimConnect_CameraEnableFlag(_handle, CameraFlag.AboveGround), "CameraEnableFlag");
        Check(SimConnect_CameraGetStatus(_handle), "CameraGetStatus");
    });

    public void ReleaseCamera() => Post(ReleaseCameraNow);

    /// <summary>Sadece SimConnect iş parçacığından (ör. AircraftData içinde) çağrılmalıdır.</summary>
    public void SetCameraNow(in DataCamera camera, CameraDataMask mask)
    {
        if (_handle == IntPtr.Zero || !CameraAcquired) return;
        SimConnect_CameraSet(_handle, camera, mask);
    }

    private void ReleaseCameraNow()
    {
        if (_handle == IntPtr.Zero || CameraState != CameraAvailability.Acquired) return;
        // Boş isim: kamerayı devralmadan önceki sim kamerasına geri döner.
        Check(SimConnect_CameraRelease(_handle, ""), "CameraRelease");
        SetCameraState(CameraAvailability.NotAcquired);
    }

    private void Run()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            if (_handle == IntPtr.Zero)
            {
                TryOpen();
                if (_handle == IntPtr.Zero)
                {
                    DrainWork();
                    token.WaitHandle.WaitOne(2000);
                    continue;
                }
            }

            _simEvent.WaitOne(100);
            DrainWork();
            Dispatch();
        }

        ReleaseCameraNow();
        CloseHandle();
    }

    private void TryOpen()
    {
        SetState(ConnectionState.Connecting);
        int hr;
        try
        {
            hr = SimConnect_Open(out _handle, ClientName, IntPtr.Zero, 0, _simEvent.SafeWaitHandle.DangerousGetHandle(), 0);
        }
        catch (DllNotFoundException)
        {
            Log?.Invoke("SimConnect.dll bulunamadı. Uygulama klasörüne kopyalandığından emin olun.");
            _handle = IntPtr.Zero;
            SetState(ConnectionState.Disconnected);
            _cts.Token.WaitHandle.WaitOne(10000);
            return;
        }

        if (hr < 0 || _handle == IntPtr.Zero)
        {
            _handle = IntPtr.Zero;
            SetState(ConnectionState.Disconnected);
            return;
        }

        for (uint i = 0; i < AircraftFields.Length; i++)
        {
            var (name, units) = AircraftFields[i];
            Check(SimConnect_AddToDataDefinition(_handle, DefAircraft, name, units, DataType.Float64, 0, Unused), $"AddToDataDefinition({name})");
        }
        Check(SimConnect_RequestDataOnSimObject(_handle, ReqAircraft, DefAircraft, ObjectIdUser, Period.VisualFrame, 0, 0, 0, 0), "RequestDataOnSimObject");
        Check(SimConnect_SubscribeToCameraStatusUpdate(_handle), "SubscribeToCameraStatusUpdate");
        Check(SimConnect_SubscribeToSystemEvent(_handle, EvtAircraftLoaded, "AircraftLoaded"), "SubscribeToSystemEvent(AircraftLoaded)");
        Check(SimConnect_RequestSystemState(_handle, ReqAircraftLoaded, "AircraftLoaded"), "RequestSystemState(AircraftLoaded)");
    }

    private void Dispatch()
    {
        while (_handle != IntPtr.Zero && SimConnect_GetNextDispatch(_handle, out var data, out _) >= 0 && data != IntPtr.Zero)
        {
            var header = Marshal.PtrToStructure<RecvHeader>(data);
            switch (header.Id)
            {
                case RecvId.Open:
                    SetState(ConnectionState.Connected);
                    Log?.Invoke("MSFS'e bağlanıldı.");
                    break;

                case RecvId.Quit:
                    Log?.Invoke("MSFS kapandı.");
                    CloseHandle();
                    return;

                case RecvId.Exception:
                    var ex = Marshal.PtrToStructure<RecvException>(data);
                    Log?.Invoke($"SimConnect hatası: kod {ex.Exception}, paket {ex.SendId}, parametre {ex.Index}");
                    break;

                case RecvId.SimObjectData:
                    var obj = Marshal.PtrToStructure<RecvSimObjectData>(data);
                    if (obj.RequestId == ReqAircraft)
                    {
                        var raw = Marshal.PtrToStructure<AircraftRaw>(data + DataOffset);
                        try { AircraftData?.Invoke(raw); }
                        catch (Exception e) { Log?.Invoke("Kamera güncelleme hatası: " + e.Message); }
                    }
                    break;

                case RecvId.SystemState:
                    // RECV_SYSTEM_STATE: başlıktan hemen sonra dwRequestID.
                    if ((uint)Marshal.ReadInt32(data, 12) == ReqAircraftLoaded)
                        RaiseAircraftLoaded(Marshal.PtrToStringAnsi(data + SystemStateStringOffset));
                    break;

                case RecvId.EventFilename:
                    // RECV_EVENT: başlık + grup + olay kimliği.
                    uint eventId = (uint)Marshal.ReadInt32(data, 16);
                    if (eventId == EvtAircraftLoaded)
                        RaiseAircraftLoaded(Marshal.PtrToStringAnsi(data + EventFilenameStringOffset));
                    break;

                case RecvId.CameraStatus:
                    var status = Marshal.PtrToStructure<RecvCameraStatus>(data);
                    SetCameraState(status.AcquiredState);
                    break;
            }
        }
    }

    private void RaiseAircraftLoaded(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Log?.Invoke("Uçak yüklendi: " + path);
        AircraftLoaded?.Invoke(path);
    }

    private void DrainWork()
    {
        while (_work.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Log?.Invoke(e.Message); }
        }
    }

    private void CloseHandle()
    {
        if (_handle != IntPtr.Zero)
        {
            SimConnect_Close(_handle);
            _handle = IntPtr.Zero;
        }
        SetCameraState(CameraAvailability.NotAcquired);
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        ConnectionChanged?.Invoke(state);
    }

    private void SetCameraState(CameraAvailability state)
    {
        if (CameraState == state) return;
        CameraState = state;
        CameraStateChanged?.Invoke(state);
    }

    private void Check(int hr, string what)
    {
        if (hr < 0) Log?.Invoke($"{what} başarısız (0x{hr:X8})");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _simEvent.Set();
        _thread.Join(2000);
        _simEvent.Dispose();
    }
}
