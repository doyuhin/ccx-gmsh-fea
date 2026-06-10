/*
 * TravelingWaveCalculator — forward/backward traveling waves for BOTH sides
 * of the seal (the "rotor/stator interactions" category).
 *
 * Each side is driven by ITS OWN Redline frequency table: own f0, own
 * geometry (R = innerDiameter/2, l), own shape. One shared speed ω₁
 * (seal redline, rev/s) and one shared K (signed by dynamic type) appear in
 * all formulas — per the legacy Excel-macro formulation.
 *
 * ============================ FORMULATION FLAG ============================
 * Cylindrical sides use the Excel/old-code formulation (seal speed ω₁ +
 * Doppler term on both sides). The reference document instead gives the
 * counterpart its own speed Kω₁ and NO Doppler term. User decision
 * 2026-06-10: follow Excel/old code, validate later. The DISC counterpart
 * follows the DOCUMENT: no observable split, Forward = Backward = f0.
 * See SealMath.cs header and DESIGN.md "Open validation item".
 * ==========================================================================
 *
 * Sparse rules: iterate actual table entries; nd < 1 skipped; absent
 * frequency → no row. Empty table → empty set (never null).
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class TravelingWaveCalculator
    {
        public static InteractionsResult Calculate(SealCriteria criteria,
            int maxNodalDiameter = int.MaxValue, int maxModesPerDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            InteractionsResult result = new InteractionsResult();

            double omega1 = SealMath.RevPerSecond(criteria.Conditions.SealSideRedlineSpeed);
            double k = SealMath.SpeedRatioK(
                criteria.CategoricalParameters.DynamicType,
                criteria.Conditions.SealSideRedlineSpeed,
                criteria.Conditions.CounterpartSideRedlineSpeed);

            // Seal (teeth) side — own table, own geometry, own shape.
            CalculateSide(
                result.Seal,
                criteria.SealNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline),
                criteria.CategoricalParameters.SealShape == SealShape.Cylindrical,
                isCounterpart: false,
                radius: criteria.GeometricParameters.supportInnerDiameter / 2.0,
                length: criteria.GeometricParameters.firstToLastLength,
                omega1: omega1, k: k,
                maxNodalDiameter: maxNodalDiameter, maxModesPerDiameter: maxModesPerDiameter);

            // Counterpart side — own table, own geometry, own shape.
            CalculateSide(
                result.Counterpart,
                criteria.CounterpartNodalFrequencyData.Table(NodalFrequencyData.AnalysisCondition.Redline),
                criteria.CategoricalParameters.CounterpartShape == CounterpartShape.Cylindrical,
                isCounterpart: true,
                radius: criteria.GeometricParameters.counterpartInnerDiameter / 2.0,
                length: criteria.GeometricParameters.counterpartFirstToLastLength,
                omega1: omega1, k: k,
                maxNodalDiameter: maxNodalDiameter, maxModesPerDiameter: maxModesPerDiameter);

            // Excitation line over the union of both sides' nodal diameters.
            foreach (int nd in result.Seal.Rows.Keys)
                result.RelativeLine[nd] = SealMath.RelativeRevolutionLine(nd, omega1, k);
            foreach (int nd in result.Counterpart.Rows.Keys)
                if (!result.RelativeLine.ContainsKey(nd))
                    result.RelativeLine[nd] = SealMath.RelativeRevolutionLine(nd, omega1, k);

            return result;
        }

        private static void CalculateSide(TravelingWaveSet target,
            NodalFrequencyData.FrequencyTable table, bool isCylindrical, bool isCounterpart,
            double radius, double length, double omega1, double k,
            int maxNodalDiameter, int maxModesPerDiameter)
        {
            foreach (int nd in table.NodalDiameters)
            {
                if (nd < 1 || nd > maxNodalDiameter) continue;   // λ singular at nd = 0

                int modesTaken = 0;
                foreach (int mode in table.Modes(nd))
                {
                    if (modesTaken >= maxModesPerDiameter) break;

                    double? f0 = table.Frequency(nd, mode);
                    if (!f0.HasValue) continue;                  // hole → no row

                    double forward, backward;
                    if (isCylindrical)
                    {
                        forward = SealMath.CylindricalForward(f0.Value, nd, omega1, k, radius, length);
                        backward = SealMath.CylindricalBackward(f0.Value, nd, omega1, k, radius, length);
                    }
                    else if (isCounterpart)
                    {
                        // Disc COUNTERPART (document): an observer on a disc
                        // cannot observe its own traveling waves — no split.
                        forward = f0.Value;
                        backward = f0.Value;
                    }
                    else
                    {
                        forward = SealMath.DiscForward(f0.Value, nd, omega1, k);
                        backward = SealMath.DiscBackward(f0.Value, nd, omega1, k);
                    }

                    target.Add(new WaveRow
                    {
                        NodalDiameter = nd,
                        Mode = mode,
                        ModalFrequency = f0.Value,
                        Forward = forward,
                        Backward = backward
                    });
                    modesTaken++;
                }
            }
        }
    }
}
