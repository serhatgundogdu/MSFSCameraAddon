namespace CinematicCam.CameraMath;

/// <summary>
/// Kritik sönümlü yay (Game Programming Gems 4, "Critically Damped Ease-In/Ease-Out").
/// Kare hızından bağımsız, aşmayan yumuşak takip sağlar.
/// </summary>
public struct SpringVec3
{
    public Vec3 Value;
    public Vec3 Velocity;
    private bool _initialized;

    public Vec3 Update(Vec3 target, double smoothTime, double dt)
    {
        if (!_initialized || smoothTime <= 1e-4)
        {
            Reset(target);
            return Value;
        }

        double omega = 2.0 / smoothTime;
        double x = omega * dt;
        double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
        Vec3 change = Value - target;
        Vec3 temp = (Velocity + change * omega) * dt;
        Velocity = (Velocity - temp * omega) * exp;
        Value = target + (change + temp) * exp;
        return Value;
    }

    public void Reset(Vec3 value)
    {
        Value = value;
        Velocity = Vec3.Zero;
        _initialized = true;
    }

    public void Invalidate() => _initialized = false;
}

public struct SpringScalar
{
    public double Value;
    public double Velocity;
    private bool _initialized;

    public double Update(double target, double smoothTime, double dt)
    {
        if (!_initialized || smoothTime <= 1e-4)
        {
            Reset(target);
            return Value;
        }

        double omega = 2.0 / smoothTime;
        double x = omega * dt;
        double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
        double change = Value - target;
        double temp = (Velocity + omega * change) * dt;
        Velocity = (Velocity - omega * temp) * exp;
        Value = target + (change + temp) * exp;
        return Value;
    }

    /// <summary>Açılar için: hedefi mevcut değere en kısa yoldan sarar.</summary>
    public double UpdateAngle(double targetDeg, double smoothTime, double dt)
    {
        if (_initialized) targetDeg = Value + Angles.Wrap180(targetDeg - Value);
        return Update(targetDeg, smoothTime, dt);
    }

    public void Reset(double value)
    {
        Value = value;
        Velocity = 0;
        _initialized = true;
    }

    public void Invalidate() => _initialized = false;
}

/// <summary>Tekrarlanabilir, yumuşak 1B değer gürültüsü (kamera sarsıntısı için).</summary>
public sealed class SmoothNoise
{
    private readonly double[] _values = new double[256];

    public SmoothNoise(int seed)
    {
        var rng = new Random(seed);
        for (int i = 0; i < _values.Length; i++) _values[i] = rng.NextDouble() * 2 - 1;
    }

    /// <summary>[-1, 1] aralığında değer.</summary>
    public double Sample(double t)
    {
        double f = Math.Floor(t);
        int i0 = (int)((long)f & 255);
        int i1 = (i0 + 1) & 255;
        double u = t - f;
        u = u * u * (3 - 2 * u);
        return _values[i0] + (_values[i1] - _values[i0]) * u;
    }

    /// <summary>Birden fazla oktav toplanmış gürültü.</summary>
    public double Fractal(double t, int octaves = 3)
    {
        double sum = 0, amp = 1, norm = 0, freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += Sample(t * freq + i * 31.7) * amp;
            norm += amp;
            amp *= 0.5;
            freq *= 2.03;
        }
        return sum / norm;
    }
}
