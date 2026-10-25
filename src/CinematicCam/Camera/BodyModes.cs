using CinematicCam.CameraMath;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>Uçağın gövdesine sabitlenmiş dış kamera (kuyruk, karın, kanat ucu…).</summary>
public sealed class FixedMode : ICameraMode
{
    public string Name => View?.Name ?? "Sabit kamera";
    public CameraPreset? View { get; set; }

    public void Activate(AircraftState s) { }

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        var v = View ?? new CameraPreset { Name = "Kuyruk", Y = 6, Z = -20, PitchDeg = -15, FovDeg = 60 };
        return new CameraPose
        {
            Frame = PoseFrame.Body,
            Offset = new Vec3(v.X, v.Y, v.Z),
            HeadingDeg = v.YawDeg + manual.YawDeg,
            PitchDeg = Math.Clamp(v.PitchDeg + manual.PitchDeg, -89, 89),
            RollDeg = v.RollDeg,
            FovDeg = Math.Clamp(v.FovDeg * manual.Zoom, 5, 120),
        };
    }
}

/// <summary>
/// Kokpit, kabin ve GoPro görünümleri. Konum pilot gözü noktasına göredir.
/// Baş hareketi, ivme ile itilme, dönüşe bakma ve gövde titreşimi ekler.
/// </summary>
public sealed class InsideMode : ICameraMode
{
    private const double Gravity = 9.80665;

    private static readonly CameraPreset DefaultPilot = new() { Name = "Pilot", Kind = PresetKind.Cockpit, PitchDeg = -8, FovDeg = 65 };

    private readonly SmoothNoise _noiseA = new(101);
    private readonly SmoothNoise _noiseB = new(211);
    private readonly SmoothNoise _noiseC = new(307);
    private SpringVec3 _pos;
    private SpringScalar _yaw, _pitch, _roll, _fov;
    private SpringVec3 _gHead;
    private double _time;
    private int _index;

    public InsideMode(InsideSettings settings) => Settings = settings;

    public InsideSettings Settings { get; set; }
    /// <summary>Aktif profildeki iç görünümler; boşsa varsayılan pilot görünümü.</summary>
    public IReadOnlyList<CameraPreset> Views { get; set; } = Array.Empty<CameraPreset>();
    public CameraPreset Current => Views.Count == 0 ? DefaultPilot : Views[Math.Clamp(_index, 0, Views.Count - 1)];
    public string Name => "İç kamera · " + Current.Name;

    public void Activate(AircraftState s) => ResetSprings();

    public void SetView(CameraPreset view)
    {
        int i = Views.ToList().FindIndex(v => v.Id == view.Id);
        if (i < 0)
        {
            Views = Views.Append(view).ToList();
            i = Views.Count - 1;
        }
        _index = i;
        if (Settings.TransitionSec <= 0) ResetSprings();
    }

    public void NextView()
    {
        if (Views.Count == 0) return;
        _index = (_index + 1) % Views.Count;
        if (Settings.TransitionSec <= 0) ResetSprings();
    }

    public void SelectRandom(Random rng)
    {
        if (Views.Count > 0) _index = rng.Next(Views.Count);
        ResetSprings();
    }

    private void ResetSprings()
    {
        _pos.Invalidate();
        _yaw.Invalidate();
        _pitch.Invalidate();
        _roll.Invalidate();
        _fov.Invalidate();
        _gHead.Invalidate();
    }

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        var c = Settings;
        var v = Current;
        _time += dt;
        double transition = Math.Max(0, c.TransitionSec) * 0.5;

        // İvme başı ters yöne iter (gövde ekseninde), ör. frende öne, kalkışta arkaya.
        var accel = new Vec3(s.AccelX, s.AccelY, s.AccelZ) / Gravity;
        var push = _gHead.Update(-accel * (0.06 * c.GForce), 0.18, dt);

        // Baş hareketi: yavaş, küçük salınım.
        double head = c.HeadMotion;
        var bob = new Vec3(_noiseA.Fractal(_time * 0.2) * 0.015, _noiseB.Fractal(_time * 0.25) * 0.01, 0) * head;

        var pos = _pos.Update(new Vec3(v.X, v.Y, v.Z), transition, dt) + push + bob;

        // Dönüşe bakma yalnızca pilot görünümlerinde anlamlı.
        double turnLook = v.Kind == PresetKind.Cockpit ? s.BankDeg * 0.4 * c.LookIntoTurn : 0;
        double yaw = _yaw.UpdateAngle(v.YawDeg + turnLook + manual.YawDeg, Math.Max(transition, 0.35), dt);
        double pitch = _pitch.Update(Math.Clamp(v.PitchDeg + manual.PitchDeg, -89, 89), Math.Max(transition, 0.2), dt);
        double roll = _roll.Update(v.RollDeg, Math.Max(transition, 0.2), dt);

        // Pilot başı ivmeyle hafifçe öne/arkaya eğilir.
        pitch += -accel.Z * 1.5 * c.GForce;
        yaw += _noiseC.Fractal(_time * 0.15) * 0.8 * head;
        pitch += _noiseB.Fractal(_time * 0.18 + 40) * 0.5 * head;

        // Gövde titreşimi: motor gücü ve pist hızıyla artar.
        double vib = c.Vibration * (0.2 * s.Throttle01 * (s.EngineRunning ? 1 : 0) + (s.OnGround ? 0.4 * Math.Clamp(s.GroundSpeedMs / 40, 0, 1) : 0));
        pitch += _noiseA.Sample(_time * 22 + 9) * vib;
        roll += _noiseC.Sample(_time * 18 + 5) * vib * 0.6;

        double fovTarget = Math.Clamp((v.FovDeg + c.FovOffsetDeg) * manual.Zoom, 20, 120);
        double fov = _fov.Update(fovTarget, Math.Max(transition, 0.15), dt);

        return new CameraPose
        {
            Frame = PoseFrame.Body,
            FromEyepoint = true,
            Offset = pos,
            HeadingDeg = yaw,
            PitchDeg = pitch,
            RollDeg = roll,
            FovDeg = fov,
        };
    }
}
