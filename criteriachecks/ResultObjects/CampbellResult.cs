/*
 * CampbellResult — output container of the Campbell check (seal/teeth side
 * only, Redline condition, first mode of each nodal diameter).
 *
 * Sparse: Rows holds one entry per nodal diameter that had a first-mode
 * frequency. Empty input → empty Rows, scalar values NaN.
 */
using System;
using System.Collections.Generic;

namespace LakeCore.CriteriaChecks
{
    public class CampbellRow
    {
        public int NodalDiameter { get; set; }
        public double FirstModeFrequency { get; set; }   // Hz, input echoed for plotting
        public double Lambda { get; set; }               // structural λ = 3R²/(n²l²)
        public double RespectiveSpeed { get; set; }      // RRS, rpm
    }

    public class CampbellResult
    {
        /// <summary>One row per nodal diameter with data, ascending nd.</summary>
        public SortedDictionary<int, CampbellRow> Rows { get; private set; }

        /// <summary>Minimum RRS over all rows [rpm]; NaN when no rows.</summary>
        public double MinRespectiveSpeed { get; set; }

        /// <summary>MinRespectiveSpeed / EngineRedlineSpeed; NaN when no rows.
        /// (Pass/fail interpretation intentionally out of scope.)</summary>
        public double Margin { get; set; }

        public bool HasData
        {
            get { return Rows.Count > 0; }
        }

        public CampbellResult()
        {
            Rows = new SortedDictionary<int, CampbellRow>();
            MinRespectiveSpeed = double.NaN;
            Margin = double.NaN;
        }
    }
}
