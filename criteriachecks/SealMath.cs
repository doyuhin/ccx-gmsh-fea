/*
 * SealMath — every formula used by the criteria checks, defined exactly once.
 *
 * Conventions:
 *   - All angular speeds enter as rpm and are converted with RevPerSecond.
 *   - Radius parameters are RADII. GeometricParameters stores diameters;
 *     calculators pass diameter/2. (The structural lambda 3R²/(n²l²) derives
 *     from inextensional shell theory where R is the cylinder radius — the
 *     original program passed the diameter straight in, making lambda 4×.)
 *   - Two DIFFERENT lambdas exist. Do not confuse them:
 *       StructuralLambda          3R²/(n²l²)    — stiffness ratio
 *                                                 (Campbell + cylindrical waves)
 *       AcousticWavelength*       2πRc/n, 2Pt/m — physical wavelength [mm]
 *                                                 (acoustic checks)
 *
 * ============================ FORMULATION FLAG ============================
 * For COUNTERPART-side cylindrical traveling waves, the reference document
 * ("3rd upload" photos) and the legacy Excel macro / original C# disagree:
 *
 *   document:    fr2_fwd/bkwd = f0₂ ± 2nKω₁/(n²+λ₂+1)      (own speed Kω₁,
 *                                                            no Doppler term)
 *   excel/old:   same function as the seal side             (seal speed ω₁,
 *                shared K, including the +nω₁(1−K) Doppler term)
 *
 * Per user decision (2026-06-10) this code follows the EXCEL/OLD-CODE
 * formulation for cylinders on BOTH sides, pending validation tests.
 * The DISC counterpart follows the DOCUMENT (no observable split — flat f0).
 * See criteriachecks/rewrite/DESIGN.md, section "Open validation item".
 * ==========================================================================
 */
using System;

namespace LakeCore
{
    public static class SealMath
    {
        /// <summary>rpm → revolutions per second (the ω used throughout).</summary>
        public static double RevPerSecond(double rpm)
        {
            return rpm / 60.0;
        }

        /// <summary>Structural lambda λ = 3R²/(n²l²). R is a RADIUS (pass D/2).
        /// Singular at nd = 0 — callers skip nd &lt; 1.</summary>
        public static double StructuralLambda(double nd, double radius, double length)
        {
            return (3.0 * Math.Pow(radius, 2)) / (Math.Pow(nd, 2) * Math.Pow(length, 2));
        }

        /// <summary>Campbell respective rotating speed [rpm]:
        /// RRS = 60·f·(n²+1+λ) / (n·(n²−1+λ)).</summary>
        public static double RespectiveRotatingSpeed(double nd, double frequency, double lambda)
        {
            return (60.0 * frequency * (Math.Pow(nd, 2) + 1.0 + lambda))
                 / (nd * (Math.Pow(nd, 2) - 1.0 + lambda));
        }

        /// <summary>Aeroelastic stability parameter:
        /// Wr = n²/((n²+1)·f²) · Δp·l·g/(4πw) · 1000.</summary>
        public static double AeroelasticWr(double nd, double frequency, double pressureDelta,
            double length, double gravitationalConstant, double effectiveVibrationalForce)
        {
            return Math.Pow(nd, 2) / ((Math.Pow(nd, 2) + 1.0) * Math.Pow(frequency, 2))
                 * pressureDelta * length * gravitationalConstant
                 / (4.0 * Math.PI * effectiveVibrationalForce) * 1000.0;
        }

        /// <summary>Speed ratio K = ω₂/ω₁, signed by dynamic type:
        /// rotor +, counter-rotor −, stator (and undefined) 0.</summary>
        public static double SpeedRatioK(CounterpartDynamicType dynamicType,
            double sealRedlineRpm, double counterpartRedlineRpm)
        {
            switch (dynamicType)
            {
                case CounterpartDynamicType.Rotor:
                    return counterpartRedlineRpm / sealRedlineRpm;
                case CounterpartDynamicType.CounterRotor:
                    return -counterpartRedlineRpm / sealRedlineRpm;
                default:
                    return 0.0;
            }
        }

        /// <summary>Cylindrical forward traveling wave (frame 2):
        /// fwd = f0 − 2nω₁/(n²+λ+1) + nω₁(1−K). See FORMULATION FLAG above.</summary>
        public static double CylindricalForward(double f0, double nd, double omega1,
            double k, double radius, double length)
        {
            double lambda = StructuralLambda(nd, radius, length);
            return f0 - (2.0 * nd * omega1) / (nd * nd + lambda + 1.0)
                      + nd * omega1 * (1.0 - k);
        }

        /// <summary>Cylindrical backward traveling wave (frame 2) — full mirror
        /// of CylindricalForward: bkwd = f0 + 2nω₁/(n²+λ+1) − nω₁(1−K).
        /// (The original C# had bkwd identical to fwd — copy-paste bug,
        /// corrected per the reference document.)</summary>
        public static double CylindricalBackward(double f0, double nd, double omega1,
            double k, double radius, double length)
        {
            double lambda = StructuralLambda(nd, radius, length);
            return f0 + (2.0 * nd * omega1) / (nd * nd + lambda + 1.0)
                      - nd * omega1 * (1.0 - k);
        }

        /// <summary>Disc forward traveling wave: fwd = f0 + nω₁(1−K).
        /// Seal side only — a disc COUNTERPART shows no split (flat f0,
        /// handled in TravelingWaveCalculator).</summary>
        public static double DiscForward(double f0, double nd, double omega1, double k)
        {
            return f0 + nd * omega1 * (1.0 - k);
        }

        /// <summary>Disc backward traveling wave: bkwd = f0 − nω₁(1−K).</summary>
        public static double DiscBackward(double f0, double nd, double omega1, double k)
        {
            return f0 - nd * omega1 * (1.0 - k);
        }

        /// <summary>Per-relative-revolution excitation line:
        /// f_relative = n·ω₁·(1−K) = n(ω₁−ω₂).</summary>
        public static double RelativeRevolutionLine(double nd, double omega1, double k)
        {
            return nd * omega1 * (1.0 - k);
        }

        /// <summary>Circumferential acoustic wavelength λg = 2π·Rc/n [mm].
        /// Singular at nd = 0 — callers skip nd &lt; 1.</summary>
        public static double AcousticWavelengthCircumferential(double grooveCentroidRc, double nd)
        {
            return 2.0 * Math.PI * (grooveCentroidRc / nd);
        }

        /// <summary>Axial acoustic wavelength λg = 2·Pt/m [mm].</summary>
        public static double AcousticWavelengthAxial(double teethPt, double mode)
        {
            return 2.0 * (teethPt / mode);
        }

        /// <summary>Acoustic frequency f = c/λg [Hz] (c in mm/s, λg in mm).</summary>
        public static double AcousticFrequency(double wavelength, double speedOfSound)
        {
            return speedOfSound / wavelength;
        }

        /// <summary>Area-weighted acoustic frequency shift:
        /// Δ = n·((ω₁_rpm/60)·A1 + (ω₂_rpm/60)·A2)/(A1+A2). Raw (unsigned)
        /// rpm values, as in the original program.</summary>
        public static double AcousticDelta(double nd, double areaA1, double areaA2,
            double sealRedlineRpm, double counterpartRedlineRpm)
        {
            return nd * ((sealRedlineRpm / 60.0) * areaA1
                       + (counterpartRedlineRpm / 60.0) * areaA2)
                      / (areaA1 + areaA2);
        }

        /// <summary>Speed of sound in air [mm/s] at temperature [°C]:
        /// c = 20037·√(T+273.15). (≈ 20.05·√T[K] in m/s, expressed in mm/s —
        /// keeps acoustic frequencies in Hz with mm wavelengths.)</summary>
        public static double SpeedOfSoundCelsius(double temperatureCelsius)
        {
            return 20037.0 * Math.Sqrt(temperatureCelsius + 273.15);
        }

        // ------------------------------------------------------------------
        // Documented-but-unused helpers from the reference document.
        // The pipeline feeds Redline frequencies from PRESTRESSED FEA, which
        // already contain centrifugal stiffening, so B·ω² is never applied.
        // Kept so the document's full equation set exists in code if static
        // (Assembly) frequencies ever need analytic spin-stiffening.
        // ------------------------------------------------------------------

        /// <summary>Cylinder spin-stiffening factor B = n²(n²−1)²/(n²+1)².
        /// UNUSED by the calculators — see note above. No disc equivalent
        /// (per user: no B for discs).</summary>
        public static double CylinderB(double nd)
        {
            double n2 = nd * nd;
            return n2 * Math.Pow(n2 - 1.0, 2) / Math.Pow(n2 + 1.0, 2);
        }

        /// <summary>Rotating natural frequency from static: fr = √(fs²+B·ω²).
        /// UNUSED by the calculators — see note above.</summary>
        public static double SpinStiffenedFrequency(double staticFrequency, double b, double omega)
        {
            return Math.Sqrt(staticFrequency * staticFrequency + b * omega * omega);
        }
    }
}
