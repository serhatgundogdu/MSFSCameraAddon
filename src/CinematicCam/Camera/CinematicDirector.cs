using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>
/// Otomatik sinematik mod: rastgele parametrelerle farklı planlar (shot) seçer ve
/// belirli sürelerle aralarında geçiş yapar.
/// </summary>
public sealed class CinematicDirector : ICameraMode
{
    private readonly Random _rng = new();
    private readonly AppSettings _settings;
    private readonly ChaseMode _chase = new(new ChaseSettings(), "Takip");
    private readonly ChaseMode _tracking = new(new ChaseSettings(), "Yandan takip");
    private readonly FlybyMode _flyby = new(new FlybySettings());
    private readonly OrbitMode _orbit = new(new OrbitSettings());
    private readonly FixedMode _fixed = new();
    private readonly InsideMode _inside;
    private readonly Func<AircraftProfile?> _profile;
    private readonly ManualInput _noManual = new();

    private ICameraMode? _current;
    private ICameraMode? _previous;
    private double _shotTime;
    private double _shotDuration;
    private bool _pendingCut;
    private bool _forceNext;

    public CinematicDirector(AppSettings settings, Func<AircraftProfile?> profile)
    {
        _settings = settings;
        _profile = profile;
        _inside = new InsideMode(settings.Inside);
    }

    public string Name => "Sinematik";
    public string CurrentShot => _current?.Name ?? "-";
    public double ShotRemainingSec => Math.Max(0, _shotDuration - _shotTime);

    public void Activate(AircraftState s) => NextShot(s);

    /// <summary>Bir sonraki karede yeni plana geçilmesini ister (iş parçacığı güvenli).</summary>
    public void RequestNextShot() => _forceNext = true;

    public CameraPose Update(AircraftState s, double dt, ManualInput manual)
    {
        _shotTime += dt;
        if (_current == null || _forceNext || _shotTime >= _shotDuration)
        {
            _forceNext = false;
            NextShot(s);
        }

        // Kullanıcı ayarlamaları sinematik modda da çalışsın.
        var pose = _current!.Update(s, dt, manual ?? _noManual);
        if (_pendingCut)
        {
            pose.Cut = true;
            _pendingCut = false;
        }
        return pose;
    }

    private void NextShot(AircraftState s)
    {
        var c = _settings.Cinematic;
        var options = new List<ICameraMode>();
        if (c.UseChase) options.Add(_chase);
        if (c.UseTracking) options.Add(_tracking);
        if (c.UseFlyby && (!s.OnGround || s.GroundSpeedMs > 10)) options.Add(_flyby);
        if (c.UseOrbit) options.Add(_orbit);

        var presets = _profile()?.Presets.Where(p => p.UseInCinematic).ToList() ?? new List<CameraPreset>();
        var fixedViews = presets.Where(p => p.Kind == PresetKind.Airframe).ToList();
        if (c.UsePresets && fixedViews.Count > 0)
        {
            _fixed.View = fixedViews[_rng.Next(fixedViews.Count)];
            options.Add(_fixed);
        }
        if (c.UseInside)
        {
            _inside.Settings = _settings.Inside;
            _inside.Views = presets.Where(p => p.Kind.IsInside()).ToList();
            _inside.SelectRandom(_rng);
            options.Add(_inside);
        }
        if (options.Count == 0) options.Add(_chase);

        // Aynı plan türünü üst üste seçmemeye çalış (sabit ve iç planlar farklı görünümlerle tekrar edebilir).
        if (options.Count > 1 && _previous != null && _previous != _fixed && _previous != _inside) options.Remove(_previous);
        var next = options[_rng.Next(options.Count)];

        Randomize(next);
        next.Activate(s);
        _previous = next;
        _current = next;
        _shotTime = 0;
        _shotDuration = c.MinShotSec + _rng.NextDouble() * Math.Max(0, c.MaxShotSec - c.MinShotSec);
        _pendingCut = true;
    }

    private double Range(double min, double max) => min + _rng.NextDouble() * (max - min);
    private double Sign() => _rng.Next(2) == 0 ? -1 : 1;

    private void Randomize(ICameraMode mode)
    {
        var baseChase = _settings.Chase;
        switch (mode)
        {
            case ChaseMode m when ReferenceEquals(m, _chase):
                m.Settings = Copy(baseChase);
                m.Settings.DistanceSpans = baseChase.DistanceSpans * Range(0.7, 1.4);
                m.Settings.HeightRatio = Range(0.05, 0.35);
                m.Settings.PositionLagSec = Range(0.3, 0.8);
                m.Settings.FovDeg = Range(35, 60);
                m.YawOffsetDeg = Range(-35, 35);
                m.ElevationOffsetDeg = 0;
                break;

            case ChaseMode m:
                // Yandan veya önden-çapraz takip.
                m.Settings = Copy(baseChase);
                m.Settings.DistanceSpans = Range(1.4, 2.6);
                m.Settings.HeightRatio = Range(-0.1, 0.2);
                m.Settings.HeadingLagSec = Range(0.8, 1.5);
                m.Settings.PositionLagSec = Range(0.5, 1.0);
                m.Settings.FollowBank = Range(0, 0.15);
                m.Settings.LookAheadSpans = 0;
                m.Settings.FovDeg = Range(30, 50);
                m.YawOffsetDeg = Sign() * Range(70, 150);
                m.ElevationOffsetDeg = 0;
                break;

            case FlybyMode f:
                var fb = _settings.Flyby;
                f.Settings = new FlybySettings
                {
                    LeadSec = fb.LeadSec * Range(0.7, 1.3),
                    SideOffsetM = fb.SideOffsetM * Range(0.5, 2.0),
                    HeightOffsetM = Range(-2, fb.HeightOffsetM * 2),
                    ResetDistanceM = fb.ResetDistanceM,
                    ZoomFollow = true,
                    Framing = Range(0.3, 0.6),
                    MinFovDeg = fb.MinFovDeg,
                    MaxFovDeg = fb.MaxFovDeg,
                    OperatorLagSec = Range(0.15, 0.45),
                };
                break;

            case OrbitMode o:
                var ob = _settings.Orbit;
                o.Settings = new OrbitSettings
                {
                    RadiusSpans = ob.RadiusSpans * Range(0.8, 1.5),
                    HeightRatio = Range(-0.15, 0.5),
                    SpeedDegS = Sign() * Range(4, 12),
                    FovDeg = Range(35, 55),
                    PositionLagSec = ob.PositionLagSec,
                };
                break;
        }
    }

    private static ChaseSettings Copy(ChaseSettings c) => new()
    {
        DistanceSpans = c.DistanceSpans,
        MinDistanceM = c.MinDistanceM,
        HeightRatio = c.HeightRatio,
        LateralM = c.LateralM,
        PositionLagSec = c.PositionLagSec,
        HeadingLagSec = c.HeadingLagSec,
        FollowPitch = c.FollowPitch,
        FollowBank = c.FollowBank,
        GForceM = c.GForceM,
        LookAheadSpans = c.LookAheadSpans,
        FovDeg = c.FovDeg,
    };
}
