/*
 * CavityAcousticResult — output container of the upstream/downstream cavity
 * acoustic coupling check.
 *
 * One row per nodal diameter for each cavity. The base cavity frequency
 * f = c/(2π·Rc) is nd-independent; only the Δ shift varies with nd. Compared
 * (on a plot, outside this scope) against the seal modal traveling waves in
 * InteractionsResult.
 */
using System;
using System.Collections.Generic;

namespace LakeCore.CriteriaChecks
{
    public class CavityAcousticRow
    {
        public int NodalDiameter { get; set; }
        public double Frequency { get; set; }    // c_cavity/(2π·Rc) [Hz]
        public double Forward { get; set; }      // f + Δ [Hz]
        public double Backward { get; set; }     // f − Δ [Hz]
    }

    public class CavityAcousticResult
    {
        /// <summary>One row per nodal diameter, ascending.</summary>
        public SortedDictionary<int, CavityAcousticRow> Upstream { get; private set; }

        /// <summary>One row per nodal diameter, ascending.</summary>
        public SortedDictionary<int, CavityAcousticRow> Downstream { get; private set; }

        public bool HasData
        {
            get { return Upstream.Count > 0 || Downstream.Count > 0; }
        }

        public CavityAcousticResult()
        {
            Upstream = new SortedDictionary<int, CavityAcousticRow>();
            Downstream = new SortedDictionary<int, CavityAcousticRow>();
        }
    }
}
