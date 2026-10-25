using CinematicCam.CameraMath;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>Uçağın etrafında sabit hızla dönen kamera.</summary>
public sealed class OrbitMode : ICameraMode
{
    private double _angle;
    private SpringVec3 _offset;

    public OrbitMode(OrbitSettings settings) => Settings = settings;

    public string Name => "Orbit";
    public OrbitSettings Settings { get; set; }

    public void Activate(AircraftState s)
    {
        _angle = s.HeadingDeg + 180;
        _offset.Invalidate();
    }

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        var c = Settings;
        _angle = Angles.Wrap360(_angle + c.SpeedDegS * dt);

        double dist = Math.Max(12, s.WingSpanM * c.RadiusSpans) * manual.Zoom;
        double elevation = Math.Clamp(Math.Atan(c.HeightRatio) * Angles.Rad2Deg + manual.PitchDeg, -80, 85);
        var target = FlightMath.Direction(_angle + manual.YawDeg, elevation) * dist;
        var offset = _offset.Update(target, c.PositionLagSec, dt);

        return CameraPose.LookAt(offset, Vec3.Zero, 0, c.FovDeg);
    }
}
