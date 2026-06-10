/*
 * ToothAcousticCalculator — tooth-only acoustic coupling frequencies.
 *
 * The acoustic frequencies depend only on geometry and speed of sound, so
 * something must choose WHICH diameters/modes to compute: rows are driven by
 * the SEAL Redline table (its nodal diameters for the circumferential set,
 * its modes at nd = 0 for the axial set) so they line up 1:1 with the modal
 * curves they're plotted against. Empty seal table → empty result.
 *
 * Circumferential, per nd:
 *   λg = 2π·Rc/nd            (honeycomb-corrected when Coating == Honeycomb)
 *   fAssembly = c_assembly/λg,  fRedline = c_redline/λg
 *   Δ = nd·((rpm₁/60)·A1 + (rpm₂/60)·A2)/(A1+A2)
 *   fwd = fRedline + Δ,  bkwd = fRedline − Δ
 *
 * Axial, per mode m at nd = 0:
 *   λg = 2·Pt/m,  fAssembly = c_assembly/λg,  fRedline = c_redline/λg
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class ToothAcousticCalculator
    {
        public static ToothAcousticResult Calculate(SealCriteria criteria,
            int maxNodalDiameter = int.MaxValue, int maxModesPerDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            ToothAcousticResult result = new ToothAcousticResult();

            NodalFrequencyData.FrequencyTable sealTable =
                criteria.SealNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline);

            GeometricParameters geo = criteria.GeometricParameters;
            Conditions cond = criteria.Conditions;
            bool honeycomb = criteria.CategoricalParameters.Coating == CounterpartCoating.Honeycomb;

            double cAssembly = cond.SpeedOfSoundAssembly;
            double cRedline = cond.SpeedOfSoundRedline;

            // --- Circumferential: one row per seal nodal diameter ---
            foreach (int nd in sealTable.NodalDiameters)
            {
                if (nd < 1 || nd > maxNodalDiameter) continue;   // λg singular at nd = 0

                double lambdaG = SealMath.AcousticWavelengthCircumferential(geo.grooveCentroidRc, nd);
                if (honeycomb)
                {
                    lambdaG = HoneycombCalculator.CorrectedWavelength(
                        geo.honeyCombParameters.a,
                        geo.honeyCombParameters.L,
                        geo.honeyCombParameters.s,
                        lambdaG);
                }

                double freqAssembly = SealMath.AcousticFrequency(lambdaG, cAssembly);
                double freqRedline = SealMath.AcousticFrequency(lambdaG, cRedline);
                double delta = SealMath.AcousticDelta(nd, geo.areaA1, geo.areaA2,
                    cond.SealSideRedlineSpeed, cond.CounterpartSideRedlineSpeed);

                result.Circumferential[nd] = new ToothAcousticCircRow
                {
                    NodalDiameter = nd,
                    WavelengthG = lambdaG,
                    FrequencyAssembly = freqAssembly,
                    FrequencyRedline = freqRedline,
                    ForwardAcoustic = freqRedline + delta,
                    BackwardAcoustic = freqRedline - delta
                };
            }

            // --- Axial: one row per mode available at nd = 0 (umbrella) ---
            int modesTaken = 0;
            foreach (int mode in sealTable.Modes(0))
            {
                if (mode < 1) continue;                          // λg singular at mode = 0
                if (modesTaken >= maxModesPerDiameter) break;

                double lambdaG = SealMath.AcousticWavelengthAxial(geo.teethPt, mode);

                result.Axial[mode] = new ToothAcousticAxialRow
                {
                    Mode = mode,
                    WavelengthG = lambdaG,
                    FrequencyAssembly = SealMath.AcousticFrequency(lambdaG, cAssembly),
                    FrequencyRedline = SealMath.AcousticFrequency(lambdaG, cRedline)
                };
                modesTaken++;
            }

            return result;
        }
    }
}
