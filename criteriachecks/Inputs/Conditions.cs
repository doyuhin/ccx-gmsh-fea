/*
 * Conditions — environmental and operating inputs for the criteria checks.
 *
 * Units:
 *   temperatures   °C
 *   speeds         rpm
 *   PressureDelta  consistent with AeroelasticWr usage (as original program)
 *   speed of sound mm/s  (SealMath.SpeedOfSoundCelsius: 20037·√(T+273.15))
 *
 * Speed-of-sound getters are computed on every access (no caching) so a
 * temperature edit in the UI is always reflected — the original lazy cache
 * went stale after the first read.
 */
using System;

namespace LakeCore
{
    public class Conditions : LakeComponent
    {
        public override string Name { get; set; } = "Conditions";
        public override LakeComponentType ToothType { get; set; } = LakeComponentType.Conditions;

        public double AirTempAssembly { get; set; } = 273.0;
        public double AirTempRedline { get; set; } = 273.0;
        public double AirTempDownstreamCavity { get; set; } = 273.0;
        public double AirTempUpstreamCavity { get; set; } = 273.0;

        public double SealSideIdleSpeed { get; set; } = 0.0;
        public double CounterpartSideIdleSpeed { get; set; } = 0.0;
        public double SealSideRedlineSpeed { get; set; } = 0.0;
        public double CounterpartSideRedlineSpeed { get; set; } = 0.0;
        public double EngineRedlineSpeed { get; set; } = 0.0;
        public double SpeedMargin { get; set; } = 0.0;
        public double EngineRedlineSpeedWithMargin { get; set; } = 0.0;

        /// <summary>Pressure difference across the seal (aeroelastic check).</summary>
        public double PressureDelta { get; set; } = 0.0;

        public double GravitationalConstant { get; set; } = 9.81;

        /// <summary>Effective vibrational force w (aeroelastic check).</summary>
        public double EffectiveVibrationalForce { get; set; } = 0.0;

        public double SpeedOfSoundAssembly
        {
            get { return SealMath.SpeedOfSoundCelsius(AirTempAssembly); }
        }

        public double SpeedOfSoundRedline
        {
            get { return SealMath.SpeedOfSoundCelsius(AirTempRedline); }
        }

        public double SpeedOfSoundUpstream
        {
            get { return SealMath.SpeedOfSoundCelsius(AirTempUpstreamCavity); }
        }

        public double SpeedOfSoundDownstream
        {
            get { return SealMath.SpeedOfSoundCelsius(AirTempDownstreamCavity); }
        }
    }
}
