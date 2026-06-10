/*
 * TravelingWaveResults — output containers of the rotor/stator traveling-wave
 * check (both sides of the seal, Redline condition, every available
 * (nd, mode) entry).
 *
 *   WaveRow            one (nd, mode): modal frequency + forward/backward wave
 *   TravelingWaveSet   all rows of ONE side, keyed [nd][mode]
 *   InteractionsResult both sides + the per-relative-revolution line
 *
 * Sparse: each side's set mirrors exactly what its own frequency table
 * contained. Get() returns null for any hole.
 */
using System;
using System.Collections.Generic;

namespace LakeCore.CriteriaChecks
{
    public class WaveRow
    {
        public int NodalDiameter { get; set; }
        public int Mode { get; set; }
        public double ModalFrequency { get; set; }   // f0 used (Redline FEA) [Hz]
        public double Forward { get; set; }          // [Hz]
        public double Backward { get; set; }         // [Hz]
    }

    public class TravelingWaveSet
    {
        /// <summary>Rows keyed [nodal diameter][mode], both ascending.</summary>
        public SortedDictionary<int, SortedDictionary<int, WaveRow>> Rows { get; private set; }

        public bool IsEmpty
        {
            get { return Rows.Count == 0; }
        }

        public TravelingWaveSet()
        {
            Rows = new SortedDictionary<int, SortedDictionary<int, WaveRow>>();
        }

        /// <summary>Row at (nd, mode), or null if absent.</summary>
        public WaveRow Get(int nodalDiameter, int mode)
        {
            SortedDictionary<int, WaveRow> modes;
            WaveRow row;
            if (Rows.TryGetValue(nodalDiameter, out modes) && modes.TryGetValue(mode, out row))
                return row;
            return null;
        }

        public void Add(WaveRow row)
        {
            SortedDictionary<int, WaveRow> modes;
            if (!Rows.TryGetValue(row.NodalDiameter, out modes))
            {
                modes = new SortedDictionary<int, WaveRow>();
                Rows[row.NodalDiameter] = modes;
            }
            modes[row.Mode] = row;
        }
    }

    public class InteractionsResult
    {
        public TravelingWaveSet Seal { get; private set; }
        public TravelingWaveSet Counterpart { get; private set; }

        /// <summary>Per-relative-revolution excitation line, one value per
        /// nodal diameter (union of both sides' diameters):
        /// f_relative = n·ω₁·(1−K).</summary>
        public SortedDictionary<int, double> RelativeLine { get; private set; }

        public InteractionsResult()
        {
            Seal = new TravelingWaveSet();
            Counterpart = new TravelingWaveSet();
            RelativeLine = new SortedDictionary<int, double>();
        }
    }
}
