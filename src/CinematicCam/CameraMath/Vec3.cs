namespace CinematicCam.CameraMath;

/// <summary>Çift hassasiyetli 3B vektör. Dünya çerçevesi ENU: X doğu, Y kuzey, Z yukarı.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double HorizontalLength => Math.Sqrt(X * X + Y * Y);
    public Vec3 Normalized => Length > 1e-9 ? this / Length : Zero;

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    // Record'un otomatik ToString'i Normalized özelliğini sonsuz özyinelemeyle yazdırır.
    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
}

public static class Angles
{
    public const double Deg2Rad = Math.PI / 180.0;
    public const double Rad2Deg = 180.0 / Math.PI;

    /// <summary>Açıyı (-180, 180] aralığına sarar.</summary>
    public static double Wrap180(double deg)
    {
        deg %= 360.0;
        if (deg > 180) deg -= 360;
        if (deg <= -180) deg += 360;
        return deg;
    }

    public static double Wrap360(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360 : deg;
    }
}
