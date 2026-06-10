/*
 * CriteriaCheckEngine — orchestrator (replaces the empty Checker stub).
 *
 * Runs every calculation category against a SealCriteria and returns one
 * CriteriaResults. Calculators never reach into each other: anything that
 * needs another category's numbers (e.g. plotting acoustic curves against
 * modal traveling waves) reads both out of the returned container.
 *
 * All checks evaluate at the Redline condition (agreed 2026-06-10); the
 * acoustic checks additionally use the Assembly speed of sound for their
 * cold curves.
 *
 * Optional caps limit how many nodal diameters / modes per diameter are
 * processed; by default everything available in the input tables is used
 * (sparse — see NodalFrequencyData).
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public static class CriteriaCheckEngine
    {
        public static CriteriaResults Run(SealCriteria criteria,
            int maxNodalDiameter = int.MaxValue, int maxModesPerDiameter = int.MaxValue)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            CriteriaResults results = new CriteriaResults();

            results.Waves = TravelingWaveCalculator.Calculate(criteria, maxNodalDiameter, maxModesPerDiameter);
            results.Campbell = CampbellCalculator.Calculate(criteria, maxNodalDiameter);
            results.Aeroelastic = AeroelasticCalculator.Calculate(criteria, maxNodalDiameter);
            results.ToothAcoustic = ToothAcousticCalculator.Calculate(criteria, maxNodalDiameter, maxModesPerDiameter);
            results.Cavity = CavityAcousticCalculator.Calculate(criteria, maxNodalDiameter);

            return results;
        }
    }
}
