using System.Runtime.InteropServices;

namespace CinematicCam.Interop;

// MSFS 2024 SDK'daki SimConnect.h ile birebir eşleşen minimal P/Invoke katmanı.
// Managed Microsoft.FlightSimulator.SimConnect.dll .NET Framework'e bağlı bir C++/CLI
// derlemesi olduğu için .NET 9'da doğrudan native SimConnect.dll kullanılır.

public enum RecvId : uint
{
    Null = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    EventFilename = 6,
    SimObjectData = 8,
    SystemState = 15,
    CameraData = 40,
    CameraStatus = 41,
}

public enum Period : uint
{
    Never = 0,
    Once = 1,
    VisualFrame = 2,
    SimFrame = 3,
    Second = 4,
}

public enum DataType : uint
{
    Int32 = 1,
    Float64 = 4,
}

public enum PositionReferential : uint
{
    None = 0,
    SimObject = 1,
    World = 2,
    Eyepoint = 3,
    SimObjectDatum = 4,
}

public enum CameraAvailability : uint
{
    NotAcquired = 0,
    Acquired = 1,
    AcquiredByOther = 2,
    UserDisabled = 3,
}

[Flags]
public enum CameraDataMask : uint
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    Targeted = 1 << 2,
    Fov = 1 << 3,
    AllRotation = Position | Rotation | Fov,
    AllTargeted = Position | Targeted | Fov,
}

[Flags]
public enum CameraFlag : uint
{
    Interaction = 0x01,
    AboveGround = 0x02,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DataXyz
{
    public double X, Y, Z;
    public DataXyz(double x, double y, double z) { X = x; Y = y; Z = z; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DataPbh
{
    public float Pitch, Bank, Heading;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct DataCamera
{
    public DataXyz Position;
    public PositionReferential PositionReferential;
    public uint PositionReferentialObjectId;
    public DataXyz TargetedPos;
    public DataPbh Pbh;
    public PositionReferential RotationReferential;
    public uint RotationReferentialObjectId;
    public double Fov; // radyan
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RecvHeader
{
    public uint Size;
    public uint Version;
    public RecvId Id;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RecvException
{
    public RecvHeader Header;
    public uint Exception;
    public uint SendId;
    public uint Index;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RecvSimObjectData
{
    public RecvHeader Header;
    public uint RequestId;
    public uint ObjectId;
    public uint DefineId;
    public uint Flags;
    public uint EntryNumber;
    public uint OutOf;
    public uint DefineCount;
    // veri burada başlar
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RecvCameraStatus
{
    public RecvHeader Header;
    public CameraAvailability AcquiredState;
    public int GameControlled;
}

internal static class SimConnectNative
{
    private const string Dll = "SimConnect.dll";

    /// <summary>SIMCONNECT_RECV_SYSTEM_STATE ve SIMCONNECT_RECV_EVENT_FILENAME içindeki metnin başlangıcı.</summary>
    public const int SystemStateStringOffset = 24;
    public const int EventFilenameStringOffset = 24;

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_RequestSystemState(IntPtr hSimConnect, uint requestId, string state);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_SubscribeToSystemEvent(IntPtr hSimConnect, uint eventId, string systemEventName);

    public const uint ObjectIdUser = 0;
    public const uint Unused = uint.MaxValue;
    public const float CameraIgnoreField = float.MaxValue;

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_Open(out IntPtr phSimConnect, string szName, IntPtr hWnd, uint userEventWin32, IntPtr hEventHandle, uint configIndex);

    [DllImport(Dll)]
    public static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport(Dll)]
    public static extern int SimConnect_GetNextDispatch(IntPtr hSimConnect, out IntPtr ppData, out uint pcbData);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_AddToDataDefinition(IntPtr hSimConnect, uint defineId, string datumName, string? unitsName, DataType datumType, float epsilon, uint datumId);

    [DllImport(Dll)]
    public static extern int SimConnect_RequestDataOnSimObject(IntPtr hSimConnect, uint requestId, uint defineId, uint objectId, Period period, uint flags, uint origin, uint interval, uint limit);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_CameraAcquire(IntPtr hSimConnect, string clientId);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int SimConnect_CameraRelease(IntPtr hSimConnect, string cameraDefName);

    [DllImport(Dll)]
    public static extern int SimConnect_CameraGetStatus(IntPtr hSimConnect);

    [DllImport(Dll)]
    public static extern int SimConnect_CameraSet(IntPtr hSimConnect, DataCamera cameraData, CameraDataMask dataMask);

    [DllImport(Dll)]
    public static extern int SimConnect_CameraEnableFlag(IntPtr hSimConnect, CameraFlag flag);

    [DllImport(Dll)]
    public static extern int SimConnect_CameraDisableFlag(IntPtr hSimConnect, CameraFlag flag);

    [DllImport(Dll)]
    public static extern int SimConnect_SubscribeToCameraStatusUpdate(IntPtr hSimConnect);
}
