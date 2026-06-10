/*
 * ToothAcousticResult — output container of the tooth-only acoustic coupling
 * check.
 *
 * Circumferential: one row per nodal diameter (driven by the seal Redline
 * table's diameters). λg is honeycomb-corrected when the counterpart coating
 * is honeycomb. Forward/backward acoustic frequencies are compared (on a
 * plot, outside this scope) against the seal modal traveling waves in
 * InteractionsResult — deliberately NOT duplicated here.
 *
 * Axial: one row per mode available at nodal diameter 0 (umbrella family).
 * No forward/backward — axial waves don't travel circumferentially. Compared
 * (on a plot) against the seal modal frequencies at nd = 0, Assembly and
 * Redline, straight from NodalFrequencyData.
 */
using System;
using System.Collections.Generic;

namespace LakeCore.CriteriaChecks
{
    public class ToothAcousticCircRow
    {
        public int NodalDiameter { get; set; }
        public double WavelengthG { get; set; }          // λg [mm], honeycomb-corrected if applicable
        public double FrequencyAssembly { get; set; }    // c_assembly/λg [Hz] (cold)
        public double FrequencyRedline { get; set; }     // c_redline/λg [Hz] (hot)
        public double ForwardAcoustic { get; set; }      // fRedline + Δ [Hz]
        public double BackwardAcoustic { get; set; }     // fRedline − Δ [Hz]
    }

    public class ToothAcousticAxialRow
    {
        public int Mode { get; set; }
        public double WavelengthG { get; set; }          // 2·Pt/mode [mm]
        public double FrequencyAssembly { get; set; }    // [Hz]
        public double FrequencyRedline { get; set; }     // [Hz]
    }

    public class ToothAcousticResult
    {
        /// <summary>One row per nodal diameter, ascending.</summary>
        public SortedDictionary<int, ToothAcousticCircRow> Circumferential { get; private set; }

        /// <summary>One row per mode at nd = 0, ascending.</summary>
        public SortedDictionary<int, ToothAcousticAxialRow> Axial { get; private set; }

        public bool HasData
        {
            get { return Circumferential.Count > 0 || Axial.Count > 0; }
        }

        public ToothAcousticResult()
        {
            Circumferential = new SortedDictionary<int, ToothAcousticCircRow>();
            Axial = new SortedDictionary<int, ToothAcousticAxialRow>();
        }
    }
}
