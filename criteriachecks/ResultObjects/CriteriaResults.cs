/*
 * CriteriaResults — the single container the plotting/reporting layer
 * consumes. Produced by CriteriaCheckEngine.Run.
 *
 * Fields are never null: an empty input yields result objects with empty
 * row collections (check HasData / IsEmpty).
 */
using System;

namespace LakeCore.CriteriaChecks
{
    public class CriteriaResults
    {
        public CampbellResult Campbell { get; set; }
        public AeroelasticResult Aeroelastic { get; set; }
        public InteractionsResult Waves { get; set; }
        public ToothAcousticResult ToothAcoustic { get; set; }
        public CavityAcousticResult Cavity { get; set; }

        public CriteriaResults()
        {
            Campbell = new CampbellResult();
            Aeroelastic = new AeroelasticResult();
            Waves = new InteractionsResult();
            ToothAcoustic = new ToothAcousticResult();
            Cavity = new CavityAcousticResult();
        }
    }
}
