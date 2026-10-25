namespace CinematicCam.CameraMath;

/// <summary>
/// Uçak gövde ekseni ile yerel ENU dünya çerçevesi arasında dönüşüm.
/// Gövde: X sağ kanat, Y yukarı, Z burun (SimConnect SimObject ofsetleriyle aynı).
/// </summary>
public readonly struct BodyFrame
{
    public readonly Vec3 Right;
    public readonly Vec3 Up;
    public readonly Vec3 Forward;

    public BodyFrame(double headingDeg, double pitchDeg, double bankDeg)
    {
        double h = headingDeg * Angles.Deg2Rad;
        double p = pitchDeg * Angles.Deg2Rad;
        double b = bankDeg * Angles.Deg2Rad;
        double sh = Math.Sin(h), ch = Math.Cos(h);
        double sp = Math.Sin(p), cp = Math.Cos(p);
        double sb = Math.Sin(b), cb = Math.Cos(b);

        Forward = new Vec3(sh * cp, ch * cp, sp);
        var right0 = new Vec3(ch, -sh, 0);
        var up0 = new Vec3(-sh * sp, -ch * sp, cp);
        Right = right0 * cb - up0 * sb;
        Up = right0 * sb + up0 * cb;
    }

    public Vec3 BodyToWorld(Vec3 body) => Right * body.X + Up * body.Y + Forward * body.Z;

    public Vec3 WorldToBody(Vec3 world) => new(Vec3.Dot(world, Right), Vec3.Dot(world, Up), Vec3.Dot(world, Forward));
}

public static class Geo
{
    private const double EarthRadius = 6378137.0;

    /// <summary>İki coğrafi nokta arasındaki ENU farkı (metre), kısa mesafeler için düz-dünya yaklaşımı.</summary>
    public static Vec3 Offset(double fromLat, double fromLon, double fromAlt, double toLat, double toLon, double toAlt)
    {
        double dLat = (toLat - fromLat) * Angles.Deg2Rad;
        double dLon = Angles.Wrap180(toLon - fromLon) * Angles.Deg2Rad;
        double north = dLat * EarthRadius;
        double east = dLon * EarthRadius * Math.Cos(fromLat * Angles.Deg2Rad);
        return new Vec3(east, north, toAlt - fromAlt);
    }

    /// <summary>Bir noktaya ENU ofset ekleyerek yeni coğrafi konum döndürür.</summary>
    public static (double Lat, double Lon, double Alt) Add(double lat, double lon, double alt, Vec3 enu)
    {
        double newLat = lat + enu.Y / EarthRadius * Angles.Rad2Deg;
        double newLon = lon + enu.X / (EarthRadius * Math.Cos(lat * Angles.Deg2Rad)) * Angles.Rad2Deg;
        return (newLat, Angles.Wrap180(newLon), alt + enu.Z);
    }

    /// <summary>Yön vektöründen baş (kuzeyden saat yönü) ve yunuslama (yukarı pozitif) açıları.</summary>
    public static (double HeadingDeg, double PitchDeg) DirectionToAngles(Vec3 dir)
    {
        double heading = Math.Atan2(dir.X, dir.Y) * Angles.Rad2Deg;
        double pitch = Math.Atan2(dir.Z, dir.HorizontalLength) * Angles.Rad2Deg;
        return (Angles.Wrap360(heading), pitch);
    }
}
