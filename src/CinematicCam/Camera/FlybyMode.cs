using CinematicCam.CameraMath;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>
/// Kamera uçağın rotası üzerinde, önünde bir noktaya sabitlenir; uçak yanından geçerken
/// kameraman gibi onu takip eder ve isteğe bağlı olarak uzaklaştıkça zoom yapar.
/// </summary>
public sealed class FlybyMode : ICameraMode
{
    private readonly Random _rng = new();
    private double _anchorLat, _anchorLon, _anchorAlt;
    private bool _hasAnchor;
    private SpringScalar _heading;
    private SpringScalar _pitch;
    private SpringScalar _fov;

    public FlybyMode(FlybySettings settings) => Settings = settings;

    public string Name => "Flyby";
    public FlybySettings Settings { get; set; }

    public void Activate(AircraftState s)
    {
        PlaceAnchor(s);
        _heading.Invalidate();
        _pitch.Invalidate();
        _fov.Invalidate();
    }

    /// <summary>Uçağın önüne yeni bir flyby noktası koyar.</summary>
    public void PlaceAnchor(AircraftState s)
    {
        var c = Settings;
        var vel = FlightMath.WorldVelocity(s);
        double side = _rng.Next(2) == 0 ? -1 : 1;
        double sideM = c.SideOffsetM * (0.7 + _rng.NextDouble() * 0.6) + s.WingSpanM * 0.5;

        Vec3 ahead;
        Vec3 right;
        if (s.GroundSpeedMs < 5)
        {
            // Duran uçak: önüne ve yanına yerleş.
            ahead = FlightMath.HorizontalForward(s.HeadingDeg) * Math.Max(30, s.WingSpanM * 2);
            right = FlightMath.HorizontalRight(s.HeadingDeg);
        }
        else
        {
            ahead = vel * c.LeadSec;
            var track = Geo.DirectionToAngles(vel).HeadingDeg;
            right = FlightMath.HorizontalRight(track);
        }

        var offset = ahead + right * (sideM * side) + Vec3.Up * c.HeightOffsetM * (0.5 + _rng.NextDouble());

        double groundAlt = s.AltitudeM - s.AltAboveGroundM;
        var (lat, lon, alt) = Geo.Add(s.LatitudeDeg, s.LongitudeDeg, s.AltitudeM, offset);
        _anchorLat = lat;
        _anchorLon = lon;
        _anchorAlt = Math.Max(alt, groundAlt + 1.7);
        _hasAnchor = true;
    }

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        if (!_hasAnchor) PlaceAnchor(s);
        var c = Settings;

        var offset = Geo.Offset(s.LatitudeDeg, s.LongitudeDeg, s.AltitudeM, _anchorLat, _anchorLon, _anchorAlt);
        double dist = offset.Length;

        // Uçak noktadan yeterince uzaklaştıysa ve uzaklaşmaya devam ediyorsa yeni nokta.
        var vel = FlightMath.WorldVelocity(s);
        if (dist > c.ResetDistanceM && Vec3.Dot(vel, offset) < 0)
        {
            PlaceAnchor(s);
            _heading.Invalidate();
            _pitch.Invalidate();
            _fov.Invalidate();
            var pose = Update(s, dt, manual);
            pose.Cut = true;
            return pose;
        }

        var (h, p) = Geo.DirectionToAngles(-offset);
        double heading = _heading.UpdateAngle(h + manual.YawDeg, c.OperatorLagSec, dt);
        double pitch = _pitch.Update(p + manual.PitchDeg, c.OperatorLagSec, dt);

        double fov = c.MaxFovDeg;
        if (c.ZoomFollow)
        {
            double size = Math.Max(s.WingSpanM, 12);
            fov = 2 * Math.Atan(size / (2 * c.Framing * Math.Max(dist, 1))) * Angles.Rad2Deg;
        }
        fov = Math.Clamp(fov * manual.Zoom, c.MinFovDeg, c.MaxFovDeg);
        fov = _fov.Update(fov, 0.5, dt);

        return new CameraPose { Offset = offset, HeadingDeg = heading, PitchDeg = pitch, RollDeg = 0, FovDeg = fov };
    }
}
