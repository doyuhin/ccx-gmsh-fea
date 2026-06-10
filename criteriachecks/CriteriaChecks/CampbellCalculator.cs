/*
 * CampbellCalculator — Campbell criterion calculation, seal (teeth) side only.
 *
 * Per nodal diameter (first mode, Redline table):
 *   λ   = 3R²/(n²l²)            R = supportInnerDiameter/2, l = firstToLastLength
 *   RRS = 60·f₁·(n²+1+λ)/(n·(n²−1+λ))
 * Margin = min(RRS)/EngineRedlineSpeed.
 *
 * Sparse rules: iterates the table's actual nodal diameters; nd < 1 skipped
 * (λ and RRS singular at nd = 0); absent first mode → no row.
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class CampbellCalculator
    {
        public static CampbellResult Calculate(SealCriteria criteria, int maxNodalDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            CampbellResult result = new CampbellResult();

            NodalFrequencyData.FrequencyTable table =
                criteria.SealNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline);

            double radius = criteria.GeometricParameters.supportInnerDiameter / 2.0;  // formula takes a RADIUS
            double length = criteria.GeometricParameters.firstToLastLength;

            foreach (int nd in table.NodalDiameters)
            {
                if (nd < 1 || nd > maxNodalDiameter) continue;

                double? firstMode = table.FirstMode(nd);
                if (!firstMode.HasValue) continue;          // hole in the data → no row

                double lambda = SealMath.StructuralLambda(nd, radius, length);
                double rrs = SealMath.RespectiveRotatingSpeed(nd, firstMode.Value, lambda);

                result.Rows[nd] = new CampbellRow
                {
                    NodalDiameter = nd,
                    FirstModeFrequency = firstMode.Value,
                    Lambda = lambda,
                    RespectiveSpeed = rrs
                };
            }

            if (result.HasData)
            {
                double min = double.PositiveInfinity;
                foreach (CampbellRow row in result.Rows.Values)
                    if (row.RespectiveSpeed < min) min = row.RespectiveSpeed;

                result.MinRespectiveSpeed = min;
                result.Margin = min / criteria.Conditions.EngineRedlineSpeed;
            }

            return result;
        }
    }
}
