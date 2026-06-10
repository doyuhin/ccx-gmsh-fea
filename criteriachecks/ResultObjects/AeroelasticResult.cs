/*
 * AeroelasticResult — output container of the aeroelastic instability check
 * (seal/teeth side only, Redline condition, first mode of each nodal
 * diameter).
 *
 * Sparse: one row per nodal diameter that had a first-mode frequency.
 * Pass/fail interpretation (threshold on Wr) intentionally out of scope.
 */
using System;
using System.Collections.Generic;

namespace LakeCore.CriteriaChecks
{
    public class AeroelasticRow
    {
        public int NodalDiameter { get; set; }
        public double FirstModeFrequency { get; set; }   // Hz, input echoed for plotting
        public double Wr { get; set; }                   // stability parameter
    }

    public class AeroelasticResult
    {
        /// <summary>One row per nodal diameter with data, ascending nd.</summary>
        public SortedDictionary<int, AeroelasticRow> Rows { get; private set; }

        public bool HasData
        {
            get { return Rows.Count > 0; }
        }

        public AeroelasticResult()
        {
            Rows = new SortedDictionary<int, AeroelasticRow>();
        }
    }
}
