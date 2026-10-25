using CinematicCam.CameraMath;
using CinematicCam.Settings;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>Motor, türbülans, yer teması ve iniş darbesinden açısal kamera sarsıntısı üretir.</summary>
public sealed class ShakeGenerator
{
    private readonly SmoothNoise _yaw = new(11);
    private readonly SmoothNoise _pitch = new(23);
    private readonly SmoothNoise _roll = new(37);

    private double _time;
    private double _accelYAvg;
    private double _turbLevel;
    private bool _wasOnGround = true;
    private double _impactTime = double.MaxValue;
    private double _impactAmp;

    public (double Yaw, double Pitch, double Roll) Update(AircraftState s, ShakeSettings c, double fovDeg, double dt)
    {
        _time += dt;

        // Türbülans: dikey ivmedeki ani sapmalar.
        double k = 1 - Math.Exp(-dt / 0.6);
        _accelYAvg += (s.AccelY - _accelYAvg) * k;
        double deviation = Math.Abs(s.AccelY - _accelYAvg);
        double targetTurb = s.OnGround ? 0 : Math.Clamp(deviation / 3.0, 0, 1);
        double attack = targetTurb > _turbLevel ? 0.08 : 0.8;
        _turbLevel += (targetTurb - _turbLevel) * (1 - Math.Exp(-dt / attack));

        // İniş darbesi.
        if (s.OnGround && !_wasOnGround && s.VerticalSpeedMs < -0.3)
        {
            _impactTime = 0;
            _impactAmp = Math.Clamp(Math.Abs(s.VerticalSpeedMs) / 3.0, 0.2, 1.5);
        }
        _wasOnGround = s.OnGround;
        _impactTime += dt;

        if (!c.Enabled) return (0, 0, 0);

        double yaw = 0, pitch = 0, roll = 0;

        // Elde tutulan kamera hissi.
        double hand = 0.35 * c.Handheld;
        yaw += _yaw.Fractal(_time * 0.25) * hand;
        pitch += _pitch.Fractal(_time * 0.3) * hand * 0.8;
        roll += _roll.Fractal(_time * 0.2) * hand * 0.5;

        // Motor titreşimi.
        double engine = s.EngineRunning ? 0.06 * c.Engine * (0.3 + 0.7 * s.Throttle01) : 0;
        yaw += _yaw.Sample(_time * 17 + 100) * engine;
        pitch += _pitch.Sample(_time * 19 + 100) * engine;

        // Türbülans.
        double turb = 1.4 * c.Turbulence * _turbLevel;
        yaw += _yaw.Fractal(_time * 2.5 + 300) * turb * 0.6;
        pitch += _pitch.Fractal(_time * 3.0 + 300) * turb;
        roll += _roll.Fractal(_time * 2.2 + 300) * turb * 0.8;

        // Pist/yer teması: hız arttıkça daha sık ve güçlü.
        if (s.OnGround)
        {
            double speed01 = Math.Clamp(s.GroundSpeedMs / 40.0, 0, 1);
            double ground = 0.3 * c.GroundRoll * speed01;
            double freq = 5 + 10 * speed01;
            pitch += _pitch.Sample(_time * freq + 500) * ground;
            roll += _roll.Sample(_time * freq * 0.8 + 500) * ground * 0.5;
        }

        // İniş darbesi: sönümlü salınım.
        if (_impactTime < 2)
        {
            double env = Math.Exp(-_impactTime * 5) * _impactAmp * 2.2 * c.Touchdown;
            pitch += Math.Sin(_impactTime * 2 * Math.PI * 6) * env;
            roll += Math.Sin(_impactTime * 2 * Math.PI * 4.3 + 1) * env * 0.4;
        }

        // Dar açılı (tele) lenslerde sarsıntıyı hafiflet, mide bulandırmasın.
        double scale = c.Master * Math.Clamp(fovDeg / 50.0, 0.3, 1.0);
        return (yaw * scale, pitch * scale, roll * scale);
    }
}
