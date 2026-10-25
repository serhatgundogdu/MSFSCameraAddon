using System.Runtime.InteropServices;

namespace CinematicCam.Sim;

/// <summary>
/// SimConnect'ten her görsel karede gelen ham veri. Alan sırası
/// <see cref="SimConnectClient"/> içindeki veri tanımıyla aynı olmalıdır.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AircraftRaw
{
    public double LatitudeDeg;
    public double LongitudeDeg;
    public double AltitudeM;
    public double AltAboveGroundM;
    public double PitchDeg;
    public double BankDeg;
    public double HeadingTrueDeg;
    public double VelBodyX;
    public double VelBodyY;
    public double VelBodyZ;
    public double VerticalSpeedMs;
    public double AccelBodyX;
    public double AccelBodyY;
    public double AccelBodyZ;
    public double RotVelBodyX;
    public double RotVelBodyY;
    public double RotVelBodyZ;
    public double OnGround;
    public double ThrottlePct;
    public double EngineCombustion;
    public double WingSpanM;
    public double GroundSpeedMs;
    public double TrueAirspeedKts;
}

/// <summary>Kamera motorunun kullandığı, iç konvansiyona çevrilmiş uçak durumu.</summary>
public sealed class AircraftState
{
    public double LatitudeDeg;
    public double LongitudeDeg;
    public double AltitudeM;
    public double AltAboveGroundM;

    /// <summary>Burun yukarı pozitif (derece).</summary>
    public double PitchDeg;
    /// <summary>Sağ kanat aşağı pozitif (derece).</summary>
    public double BankDeg;
    /// <summary>Kuzeyden saat yönünde gerçek baş (derece).</summary>
    public double HeadingDeg;

    public double ForwardSpeedMs;
    public double VerticalSpeedMs;
    public double GroundSpeedMs;
    public double TrueAirspeedKts;
    /// <summary>Gövde ekseninde ivme (x sağ, y yukarı, z ileri), m/s².</summary>
    public double AccelX, AccelY, AccelZ;
    /// <summary>Yaw hızı (derece/sn).</summary>
    public double YawRateDegS;
    public bool OnGround;
    public double Throttle01;
    public bool EngineRunning;
    public double WingSpanM;

    public static AircraftState From(in AircraftRaw r, bool invertPitch, bool invertBank)
    {
        // SimConnect konvansiyonu: pitch burun aşağı pozitif, bank sol kanat aşağı pozitif.
        // İç konvansiyon havacılık standardıdır; ters görünürse ayarlardan çevrilebilir.
        double pitch = -r.PitchDeg;
        double bank = -r.BankDeg;
        if (invertPitch) pitch = -pitch;
        if (invertBank) bank = -bank;

        return new AircraftState
        {
            LatitudeDeg = r.LatitudeDeg,
            LongitudeDeg = r.LongitudeDeg,
            AltitudeM = r.AltitudeM,
            AltAboveGroundM = r.AltAboveGroundM,
            PitchDeg = pitch,
            BankDeg = bank,
            HeadingDeg = r.HeadingTrueDeg,
            ForwardSpeedMs = r.VelBodyZ,
            VerticalSpeedMs = r.VerticalSpeedMs,
            GroundSpeedMs = r.GroundSpeedMs,
            TrueAirspeedKts = r.TrueAirspeedKts,
            AccelX = r.AccelBodyX,
            AccelY = r.AccelBodyY,
            AccelZ = r.AccelBodyZ,
            YawRateDegS = r.RotVelBodyY,
            OnGround = r.OnGround > 0.5,
            Throttle01 = Math.Clamp(r.ThrottlePct / 100.0, 0, 1),
            EngineRunning = r.EngineCombustion > 0.5,
            WingSpanM = r.WingSpanM > 1 ? r.WingSpanM : 11,
        };
    }
}
