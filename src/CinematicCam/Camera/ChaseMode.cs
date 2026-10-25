using CinematicCam.CameraMath;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>
/// Uçağı arkadan (veya verilen açıdan) yaylı şekilde takip eden kamera.
/// Baş gecikmeli takip edilir, ivme kamerayı iter (G hissi), ufuk büyük ölçüde sabit kalır.
/// </summary>
public sealed class ChaseMode : ICameraMode
{
    private const double Gravity = 9.80665;

    private SpringScalar _heading;
    private SpringScalar _pitch;
    private SpringScalar _roll;
    private SpringVec3 _offset;
    private SpringVec3 _gPush;

    public ChaseMode(ChaseSettings settings, string name = "Chase")
    {
        Settings = settings;
        Name = name;
    }

    public string Name { get; }
    public ChaseSettings Settings { get; set; }
    /// <summary>Takip açısına eklenen sabit baş ofseti (ör. yandan takip için 90°).</summary>
    public double YawOffsetDeg { get; set; }
    public double ElevationOffsetDeg { get; set; }

    public void Activate(AircraftState s)
    {
        _heading.Invalidate();
        _pitch.Invalidate();
        _roll.Invalidate();
        _offset.Invalidate();
        _gPush.Invalidate();
    }

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        var c = Settings;
        double span = s.WingSpanM;
        double dist = Math.Max(c.MinDistanceM, span * c.DistanceSpans) * manual.Zoom;

        double heading = _heading.UpdateAngle(s.HeadingDeg, c.HeadingLagSec, dt);
        double pitchFollow = _pitch.Update(s.PitchDeg * c.FollowPitch, c.HeadingLagSec, dt);

        // Kamera, uçağın arkasında bir küre üzerinde durur.
        double camYaw = heading + 180 + YawOffsetDeg + manual.YawDeg;
        double elevation = Math.Atan(c.HeightRatio) * Angles.Rad2Deg + ElevationOffsetDeg + manual.PitchDeg - pitchFollow;
        elevation = Math.Clamp(elevation, -80, 85);

        var target = FlightMath.Direction(camYaw, elevation) * dist + FlightMath.HorizontalRight(heading) * c.LateralM;

        // İvme kamerayı ters yöne iter: hızlanırken geride kalır, dönüşte dışa kayar.
        var frame = new BodyFrame(s.HeadingDeg, s.PitchDeg, s.BankDeg);
        var accelWorld = frame.BodyToWorld(new Vec3(s.AccelX, s.AccelY, s.AccelZ));
        var push = -accelWorld / Gravity * c.GForceM;
        double maxPush = c.GForceM * 1.5;
        if (push.Length > maxPush) push = push.Normalized * maxPush;
        push = _gPush.Update(push, 0.25, dt);

        var offset = _offset.Update(target + push, c.PositionLagSec, dt);

        var lookTarget = frame.Forward * (span * c.LookAheadSpans);
        double roll = _roll.Update(s.BankDeg * c.FollowBank, 0.4, dt);

        return CameraPose.LookAt(offset, lookTarget, roll, c.FovDeg);
    }
}
