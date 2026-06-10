/*
 * AeroelasticCalculator — aeroelastic instability parameter, seal (teeth)
 * side only.
 *
 * Per nodal diameter (first mode, Redline table):
 *   Wr = n²/((n²+1)·f₁²) · Δp·l·g/(4πw) · 1000
 * with Δp = Conditions.PressureDelta, l = firstToLastLength,
 * g = GravitationalConstant, w = EffectiveVibrationalForce.
 *
 * Sparse rules: iterates the table's actual nodal diameters; nd < 1 skipped;
 * absent first mode → no row.
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class AeroelasticCalculator
    {
        public static AeroelasticResult Calculate(SealCriteria criteria, int maxNodalDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            AeroelasticResult result = new AeroelasticResult();

            NodalFrequencyData.FrequencyTable table =
                criteria.SealNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline);

            double length = criteria.GeometricParameters.firstToLastLength;
            double pressureDelta = criteria.Conditions.PressureDelta;
            double g = criteria.Conditions.GravitationalConstant;
            double w = criteria.Conditions.EffectiveVibrationalForce;

            foreach (int nd in table.NodalDiameters)
            {
                if (nd < 1 || nd > maxNodalDiameter) continue;

                double? firstMode = table.FirstMode(nd);
                if (!firstMode.HasValue) continue;          // hole in the data → no row

                result.Rows[nd] = new AeroelasticRow
                {
                    NodalDiameter = nd,
                    FirstModeFrequency = firstMode.Value,
                    Wr = SealMath.AeroelasticWr(nd, firstMode.Value, pressureDelta, length, g, w)
                };
            }

            return result;
        }
    }
}
