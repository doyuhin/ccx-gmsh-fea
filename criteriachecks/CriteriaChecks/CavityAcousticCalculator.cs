/*
 * CavityAcousticCalculator — upstream/downstream cavity acoustic frequencies.
 *
 * Rows are driven by the SEAL Redline table's nodal diameters (same reason
 * as ToothAcousticCalculator: 1:1 alignment with the modal curves they're
 * plotted against). Per nd and cavity:
 *
 *   f    = c_cavity / (2π·Rc)        (c from the cavity's air temperature)
 *   Δ    = nd·((rpm₁/60)·A1 + (rpm₂/60)·A2)/(A1+A2)
 *   fwd  = f + Δ
 *   bkwd = f − Δ
 *
 * Sign convention unified with the tooth acoustic check (fwd = +Δ); the
 * original program had the cavity signs flipped — agreed as a quirk to fix
 * (user, 2026-06-10).
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class CavityAcousticCalculator
    {
        public static CavityAcousticResult Calculate(SealCriteria criteria, int maxNodalDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            CavityAcousticResult result = new CavityAcousticResult();

            NodalFrequencyData.FrequencyTable sealTable =
                criteria.SealNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline);

            GeometricParameters geo = criteria.GeometricParameters;
            Conditions cond = criteria.Conditions;

            double circumference = 2.0 * Math.PI * geo.grooveCentroidRc;
            double upstreamFrequency = cond.SpeedOfSoundUpstream / circumference;
            double downstreamFrequency = cond.SpeedOfSoundDownstream / circumference;

            foreach (int nd in sealTable.NodalDiameters)
            {
                if (nd < 1 || nd > maxNodalDiameter) continue;

                double delta = SealMath.AcousticDelta(nd, geo.areaA1, geo.areaA2,
                    cond.SealSideRedlineSpeed, cond.CounterpartSideRedlineSpeed);

                result.Upstream[nd] = new CavityAcousticRow
                {
                    NodalDiameter = nd,
                    Frequency = upstreamFrequency,
                    Forward = upstreamFrequency + delta,
                    Backward = upstreamFrequency - delta
                };

                result.Downstream[nd] = new CavityAcousticRow
                {
                    NodalDiameter = nd,
                    Frequency = downstreamFrequency,
                    Forward = downstreamFrequency + delta,
                    Backward = downstreamFrequency - delta
                };
            }

            return result;
        }
    }
}
