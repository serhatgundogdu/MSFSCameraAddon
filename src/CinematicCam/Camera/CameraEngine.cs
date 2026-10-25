using System.Collections.Concurrent;
using System.Diagnostics;
using CinematicCam.CameraMath;
using CinematicCam.Interop;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>Arayüze gösterilecek anlık durum.</summary>
public sealed record Telemetry(
    AircraftState Aircraft,
    CameraPose Pose,
    Vec3 BodyOffset,
    CameraModeKind Mode,
    string ModeName,
    string Detail,
    double FrameRate);

/// <summary>
/// Her sim karesinde aktif kamera modunu çalıştırır, geçiş ve sarsıntıyı uygular
/// ve sonucu SimConnect kamera API'sine gönderir. SimConnect iş parçacığında çalışır;
/// arayüzden gelen komutlar kuyruğa alınıp bir sonraki karede uygulanır.
/// </summary>
public sealed class CameraEngine
{
    private const double ModeSwitchBlendSec = 0.8;

    private readonly SimConnectClient _client;
    private readonly AppSettings _settings;
    private readonly Dictionary<CameraModeKind, ICameraMode> _modes;
    private readonly CinematicDirector _director;
    private readonly FlybyMode _flyby;
    private readonly InsideMode _inside;
    private readonly FixedMode _fixed = new();
    private readonly ShakeGenerator _shake = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<Action<AircraftState>> _commands = new();

    private volatile bool _enabled;
    private volatile bool _modeDirty = true;
    private volatile AircraftProfile? _profile;
    private CameraModeKind _activeKind;
    private ICameraMode? _active;
    private AircraftState? _lastState;
    private CameraPose _lastPose;
    private bool _hasLastPose;
    private CameraPose _blendFrom;
    private double _blendTime = double.MaxValue;
    private double _blendDuration;
    private double _lastTime;
    private double _fps;
    private bool _wasAcquired;

    public CameraEngine(SimConnectClient client, AppSettings settings)
    {
        _client = client;
        _settings = settings;
        _flyby = new FlybyMode(settings.Flyby);
        _inside = new InsideMode(settings.Inside);
        _director = new CinematicDirector(settings, () => _profile);
        _modes = new()
        {
            [CameraModeKind.Chase] = new ChaseMode(settings.Chase),
            [CameraModeKind.Flyby] = _flyby,
            [CameraModeKind.Orbit] = new OrbitMode(settings.Orbit),
            [CameraModeKind.Inside] = _inside,
            [CameraModeKind.Cinematic] = _director,
            [CameraModeKind.Fixed] = _fixed,
        };
        _client.AircraftData += OnAircraftData;
        _client.CameraStateChanged += OnCameraStateChanged;
        _client.AircraftLoaded += OnAircraftLoaded;
        if (_settings.Mode == CameraModeKind.Fixed) _settings.Mode = CameraModeKind.Chase;
        RefreshProfile();
    }

    public ManualInput Manual { get; } = new();
    public Telemetry? Latest { get; private set; }
    public bool Enabled => _enabled;
    public AircraftProfile? Profile => _profile;
    public string? LoadedAircraftPath { get; private set; }
    /// <summary>Simdeki uçakla eşleşen profil (sabitlenmiş profilden bağımsız).</summary>
    public AircraftProfile? MatchedProfile { get; private set; }
    public string? ActivePresetId { get; private set; }

    public event Action<bool>? EnabledChanged;
    public event Action? ProfileChanged;

    // ---------------- Açma / kapama ----------------

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        EnabledChanged?.Invoke(enabled);
        if (enabled)
        {
            _modeDirty = true;
            _hasLastPose = false;
            _client.AcquireCamera();
        }
        else
        {
            _client.ReleaseCamera();
        }
    }

    private void OnCameraStateChanged(CameraAvailability state)
    {
        // Kullanıcı simde başka bir kameraya geçerse veya bağlantı koparsa kamerayı kapalı say.
        if (state == CameraAvailability.Acquired) _wasAcquired = true;
        else if (_wasAcquired)
        {
            _wasAcquired = false;
            if (_enabled)
            {
                _enabled = false;
                EnabledChanged?.Invoke(false);
            }
        }
    }

    // ---------------- Profil ----------------

    private void OnAircraftLoaded(string path)
    {
        LoadedAircraftPath = path;
        RefreshProfile();
    }

    /// <summary>Sabitlenmiş profili veya simdeki uçakla eşleşeni etkinleştirir.</summary>
    public void RefreshProfile()
    {
        MatchedProfile = LoadedAircraftPath == null
            ? null
            : _settings.Profiles.FirstOrDefault(p => p.AutoActivate && p.Matches(LoadedAircraftPath));
        var pinned = _settings.PinnedProfileKey == null
            ? null
            : _settings.Profiles.FirstOrDefault(p => p.Key == _settings.PinnedProfileKey);
        var next = pinned ?? MatchedProfile;
        bool changed = !ReferenceEquals(next, _profile);
        _profile = next;
        Post(_ => _inside.Views = InsideViews());
        if (changed) ProfileChanged?.Invoke();
    }

    private List<CameraPreset> InsideViews() => _profile?.Presets.Where(p => p.Kind.IsInside()).ToList() ?? new List<CameraPreset>();

    // ---------------- Komutlar (her iş parçacığından çağrılabilir) ----------------

    private void Post(Action<AircraftState> command) => _commands.Enqueue(command);

    public void SetMode(CameraModeKind kind)
    {
        _settings.Mode = kind;
        if (kind != CameraModeKind.Fixed && kind != CameraModeKind.Inside) ActivePresetId = null;
        _modeDirty = true;
    }

    /// <summary>Moda göre "sonraki": yeni flyby noktası, sonraki iç görünüm veya sonraki sinematik plan.</summary>
    public void Next() => Post(s =>
    {
        switch (_activeKind)
        {
            case CameraModeKind.Flyby:
                _flyby.Activate(s);
                StartBlend(0);
                break;
            case CameraModeKind.Inside:
                _inside.NextView();
                ActivePresetId = _inside.Current.Id;
                break;
            case CameraModeKind.Cinematic:
                _director.RequestNextShot();
                break;
        }
    });

    public void ReverseOrbit() => _settings.Orbit.SpeedDegS = -_settings.Orbit.SpeedDegS;

    public void Rotate(double yawDeg, double pitchDeg)
    {
        Manual.YawDeg = Angles.Wrap180(Manual.YawDeg + yawDeg);
        Manual.PitchDeg = Math.Clamp(Manual.PitchDeg + pitchDeg, -80, 80);
    }

    public void Zoom(double factor) => Manual.Zoom = Math.Clamp(Manual.Zoom * factor, 0.25, 6);

    /// <summary>Bir uçak preset'ini kameraya uygular; türüne göre uygun moda geçer.</summary>
    public void ApplyCameraPreset(CameraPreset p)
    {
        ActivePresetId = p.Id;
        Manual.Reset();
        switch (p.Kind)
        {
            case PresetKind.Airframe:
                Post(_ => _fixed.View = p);
                SetMode(CameraModeKind.Fixed);
                break;

            case PresetKind.Cockpit:
            case PresetKind.Cabin:
            case PresetKind.InsideMounted:
                Post(_ => _inside.SetView(p));
                if (_settings.Mode != CameraModeKind.Inside) SetMode(CameraModeKind.Inside);
                ActivePresetId = p.Id;
                break;

            case PresetKind.Chase:
            case PresetKind.Orbit:
                double span = _lastState?.WingSpanM ?? 11;
                double horiz = Math.Max(1, Math.Sqrt(p.X * p.X + p.Z * p.Z));
                // Kamera konumunun "tam arka"ya göre açısı.
                double yaw = Angles.Wrap180(Math.Atan2(p.X, p.Z) * Angles.Rad2Deg - 180);
                if (p.Kind == PresetKind.Chase)
                {
                    _settings.Chase.DistanceSpans = horiz / span;
                    _settings.Chase.HeightRatio = p.Y / horiz;
                    _settings.Chase.FovDeg = p.FovDeg;
                    Manual.YawDeg = yaw;
                    SetMode(CameraModeKind.Chase);
                }
                else
                {
                    _settings.Orbit.RadiusSpans = horiz / span;
                    _settings.Orbit.HeightRatio = p.Y / horiz;
                    _settings.Orbit.FovDeg = p.FovDeg;
                    SetMode(CameraModeKind.Orbit);
                }
                ActivePresetId = p.Id;
                break;
        }
    }

    /// <summary>Mevcut görünümü (sabit/iç kamerada) yeni bir preset olarak döndürür.</summary>
    public CameraPreset? CaptureCurrentView(string name)
    {
        var t = Latest;
        if (t == null || t.Pose.Frame != PoseFrame.Body) return null;
        return new CameraPreset
        {
            Name = name,
            Kind = t.Pose.FromEyepoint ? PresetKind.Cabin : PresetKind.Airframe,
            X = t.Pose.Offset.X, Y = t.Pose.Offset.Y, Z = t.Pose.Offset.Z,
            PitchDeg = t.Pose.PitchDeg, YawDeg = t.Pose.HeadingDeg, RollDeg = t.Pose.RollDeg,
            FovDeg = t.Pose.FovDeg,
        };
    }

    public Preset CapturePreset(string name) => new()
    {
        Name = name,
        Mode = _settings.Mode == CameraModeKind.Fixed ? CameraModeKind.Chase : _settings.Mode,
        ManualYawDeg = Manual.YawDeg,
        ManualPitchDeg = Manual.PitchDeg,
        ManualZoom = Manual.Zoom,
        Chase = Clone(_settings.Chase),
        Orbit = Clone(_settings.Orbit),
    };

    public void ApplyPreset(Preset p)
    {
        Copy(p.Chase, _settings.Chase);
        Copy(p.Orbit, _settings.Orbit);
        Manual.YawDeg = p.ManualYawDeg;
        Manual.PitchDeg = p.ManualPitchDeg;
        Manual.Zoom = p.ManualZoom;
        SetMode(p.Mode);
    }

    // ---------------- Kare döngüsü ----------------

    private void OnAircraftData(AircraftRaw raw)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 1e-4, 0.1);
        _lastTime = now;
        _fps += (1.0 / dt - _fps) * 0.05;

        var cal = _settings.Calibration;
        var s = AircraftState.From(raw, cal.InvertAircraftPitch, cal.InvertAircraftBank);
        _lastState = s;

        while (_commands.TryDequeue(out var command)) command(s);

        if (!_enabled || !_client.CameraAcquired)
        {
            Latest = new Telemetry(s, default, Vec3.Zero, _settings.Mode, ModeLabel(_settings.Mode), "-", _fps);
            return;
        }

        if (_modeDirty || _active == null)
        {
            _modeDirty = false;
            _activeKind = _settings.Mode;
            _active = _modes[_activeKind];
            _active.Activate(s);
            StartBlend(ModeSwitchBlendSec);
        }

        var pose = _active.Update(s, dt, Manual);
        if (pose.Cut) StartBlend(_settings.Cinematic.BlendSec);

        if (_blendTime < _blendDuration)
        {
            _blendTime += dt;
            double t = Math.Clamp(_blendTime / _blendDuration, 0, 1);
            t = t * t * (3 - 2 * t);
            pose = CameraPose.Blend(_blendFrom, pose, t);
        }

        _lastPose = pose;
        _hasLastPose = true;

        var (sy, sp, sr) = _shake.Update(s, _settings.Shake, pose.FovDeg, dt);
        var output = pose;
        output.HeadingDeg += sy;
        output.PitchDeg += sp;
        output.RollDeg += sr;

        var camera = CameraOutput.Build(output, s, cal, out var mask, out var bodyOffset);
        _client.SetCameraNow(camera, mask);

        Latest = new Telemetry(s, output, bodyOffset, _activeKind, _active.Name, Detail(output), _fps);
    }

    private string Detail(in CameraPose pose) => _activeKind switch
    {
        CameraModeKind.Cinematic => $"{_director.CurrentShot} · {_director.ShotRemainingSec:0} sn",
        CameraModeKind.Inside => _inside.Current.Name,
        CameraModeKind.Fixed => _fixed.Name,
        _ => $"{pose.Offset.Length:0} m",
    };

    public static string ModeLabel(CameraModeKind kind) => kind switch
    {
        CameraModeKind.Inside => "İç kamera",
        CameraModeKind.Cinematic => "Sinematik",
        CameraModeKind.Fixed => "Sabit kamera",
        _ => kind.ToString(),
    };

    private void StartBlend(double duration)
    {
        if (duration <= 0 || !_hasLastPose)
        {
            _blendTime = double.MaxValue;
            return;
        }
        _blendFrom = _lastPose;
        _blendTime = 0;
        _blendDuration = duration;
    }

    private static ChaseSettings Clone(ChaseSettings c)
    {
        var n = new ChaseSettings();
        Copy(c, n);
        return n;
    }

    private static OrbitSettings Clone(OrbitSettings c)
    {
        var n = new OrbitSettings();
        Copy(c, n);
        return n;
    }

    private static void Copy(ChaseSettings from, ChaseSettings to)
    {
        to.DistanceSpans = from.DistanceSpans;
        to.MinDistanceM = from.MinDistanceM;
        to.HeightRatio = from.HeightRatio;
        to.LateralM = from.LateralM;
        to.PositionLagSec = from.PositionLagSec;
        to.HeadingLagSec = from.HeadingLagSec;
        to.FollowPitch = from.FollowPitch;
        to.FollowBank = from.FollowBank;
        to.GForceM = from.GForceM;
        to.LookAheadSpans = from.LookAheadSpans;
        to.FovDeg = from.FovDeg;
    }

    private static void Copy(OrbitSettings from, OrbitSettings to)
    {
        to.RadiusSpans = from.RadiusSpans;
        to.HeightRatio = from.HeightRatio;
        to.SpeedDegS = from.SpeedDegS;
        to.FovDeg = from.FovDeg;
        to.PositionLagSec = from.PositionLagSec;
    }
}

/// <summary>İç kamera pozunu SimConnect kamera yapısına çevirir.</summary>
public static class CameraOutput
{
    public static DataCamera Build(in CameraPose pose, AircraftState s, CalibrationSettings cal, out CameraDataMask mask, out Vec3 bodyOffset)
    {
        var cam = new DataCamera
        {
            PositionReferentialObjectId = SimConnectNative.ObjectIdUser,
            RotationReferentialObjectId = SimConnectNative.ObjectIdUser,
            Fov = Math.Clamp(pose.FovDeg, 2, 120) * Angles.Deg2Rad,
        };

        // SimConnect konvansiyonu: yunuslama burun aşağı pozitif, yatış sola pozitif.
        float SimPitch(double p) => (float)(cal.InvertCameraPitch ? p : -p);
        float SimBank(double r) => (float)(cal.InvertCameraBank ? r : -r);

        if (pose.Frame == PoseFrame.Body)
        {
            // Sabit ve iç kameralar: konum ve açılar doğrudan uçağa göre.
            bodyOffset = pose.Offset;
            cam.Position = new DataXyz(pose.Offset.X, pose.Offset.Y, pose.Offset.Z);
            cam.PositionReferential = pose.FromEyepoint ? PositionReferential.Eyepoint : PositionReferential.SimObject;
            cam.RotationReferential = PositionReferential.SimObject;
            cam.Pbh = new DataPbh { Pitch = SimPitch(pose.PitchDeg), Bank = SimBank(pose.RollDeg), Heading = (float)Angles.Wrap180(pose.HeadingDeg) };
            mask = CameraDataMask.AllRotation;
            return cam;
        }

        // Konum uçağa bağlı gönderilir: böylece yüksek hızda bile kamera titremez.
        var frame = new BodyFrame(s.HeadingDeg, s.PitchDeg, s.BankDeg);
        bodyOffset = frame.WorldToBody(pose.Offset);
        cam.Position = new DataXyz(bodyOffset.X, bodyOffset.Y, bodyOffset.Z);
        cam.PositionReferential = PositionReferential.SimObject;

        if (cal.RotationMode == RotationOutputMode.TargetedSimObject)
        {
            var dir = FlightMath.Direction(pose.HeadingDeg, pose.PitchDeg);
            var target = frame.WorldToBody(pose.Offset + dir * 200);
            cam.TargetedPos = new DataXyz(target.X, target.Y, target.Z);
            cam.RotationReferential = PositionReferential.SimObject;
            mask = CameraDataMask.AllTargeted;
        }
        else
        {
            cam.Pbh = new DataPbh { Pitch = SimPitch(pose.PitchDeg), Bank = SimBank(pose.RollDeg), Heading = (float)Angles.Wrap360(pose.HeadingDeg) };
            cam.RotationReferential = PositionReferential.World;
            mask = CameraDataMask.AllRotation;
        }

        return cam;
    }
}
