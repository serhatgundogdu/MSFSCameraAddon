using CinematicCam.CameraMath;
using CinematicCam.Sim;

namespace CinematicCam.Camera;

/// <summary>
/// Bir kameranın uçağa göre pozu. Konum uçak merkezine göre ENU (metre),
/// açılar dünya çerçevesinde (baş kuzeyden saat yönü, yunuslama yukarı pozitif, yatış sağa pozitif).
/// </summary>
public enum PoseFrame
{
    /// <summary>Konum uçağa göre ENU, açılar dünyaya göre.</summary>
    World,
    /// <summary>Konum ve açılar uçağın gövde eksenine göre (sabit ve iç kameralar).</summary>
    Body,
}

public struct CameraPose
{
    public PoseFrame Frame;
    /// <summary>Body çerçevesinde konumun referansı: uçak veya pilot gözü.</summary>
    public bool FromEyepoint;
    /// <summary>World: ENU ofset. Body: gövde ofseti (x sağ, y yukarı, z ileri).</summary>
    public Vec3 Offset;
    public double HeadingDeg;
    public double PitchDeg;
    public double RollDeg;
    public double FovDeg;
    /// <summary>Bu karede yeni bir plana (shot) geçildi.</summary>
    public bool Cut;

    public static CameraPose LookAt(Vec3 offset, Vec3 target, double rollDeg, double fovDeg)
    {
        var (h, p) = Geo.DirectionToAngles(target - offset);
        return new CameraPose { Offset = offset, HeadingDeg = h, PitchDeg = p, RollDeg = rollDeg, FovDeg = fovDeg };
    }

    public static CameraPose Blend(in CameraPose a, in CameraPose b, double t)
    {
        // Farklı çerçeveler arası karıştırma anlamsız: doğrudan kes.
        if (a.Frame != b.Frame || a.FromEyepoint != b.FromEyepoint) return b;
        return new CameraPose
        {
            Frame = b.Frame,
            FromEyepoint = b.FromEyepoint,
            Offset = Vec3.Lerp(a.Offset, b.Offset, t),
            HeadingDeg = a.HeadingDeg + Angles.Wrap180(b.HeadingDeg - a.HeadingDeg) * t,
            PitchDeg = a.PitchDeg + (b.PitchDeg - a.PitchDeg) * t,
            RollDeg = a.RollDeg + (b.RollDeg - a.RollDeg) * t,
            FovDeg = a.FovDeg + (b.FovDeg - a.FovDeg) * t,
        };
    }
}

/// <summary>Kullanıcının kısayollarla verdiği anlık ayarlamalar.</summary>
public sealed class ManualInput
{
    public double YawDeg;
    public double PitchDeg;
    /// <summary>Mesafe/zoom çarpanı (1 = varsayılan).</summary>
    public double Zoom = 1;

    public void Reset()
    {
        YawDeg = 0;
        PitchDeg = 0;
        Zoom = 1;
    }
}

public interface ICameraMode
{
    string Name { get; }
    /// <summary>Mod etkinleştiğinde; yayları sıfırlar ve gerekiyorsa kamera noktası seçer.</summary>
    void Activate(AircraftState s);
    CameraPose Update(AircraftState s, double dt, ManualInput manual);
}

internal static class FlightMath
{
    /// <summary>Uçağın dünya hızı (ENU, m/s), yer hızı ve dikey hızdan.</summary>
    public static Vec3 WorldVelocity(AircraftState s)
    {
        double h = s.HeadingDeg * Angles.Deg2Rad;
        return new Vec3(Math.Sin(h) * s.GroundSpeedMs, Math.Cos(h) * s.GroundSpeedMs, s.VerticalSpeedMs);
    }

    public static Vec3 HorizontalForward(double headingDeg)
    {
        double h = headingDeg * Angles.Deg2Rad;
        return new Vec3(Math.Sin(h), Math.Cos(h), 0);
    }

    public static Vec3 HorizontalRight(double headingDeg)
    {
        double h = headingDeg * Angles.Deg2Rad;
        return new Vec3(Math.Cos(h), -Math.Sin(h), 0);
    }

    /// <summary>Verilen baş ve yükselme açısında birim yön.</summary>
    public static Vec3 Direction(double headingDeg, double elevationDeg)
    {
        double e = elevationDeg * Angles.Deg2Rad;
        return HorizontalForward(headingDeg) * Math.Cos(e) + Vec3.Up * Math.Sin(e);
    }
}
