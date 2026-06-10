/*
 * NodalFrequencyData — modal frequency input for ONE side of the seal
 * (seal/teeth side or counterpart side), at two analysis conditions
 * (Assembly = cold/static, Redline = hot, prestressed FEA).
 *
 * SPARSE-DATA CONTRACT (load-bearing for every calculator):
 *   - Only entries that were actually added exist. There may be any number
 *     of nodal diameters, each with any subset of modes.
 *   - Frequency()/FirstMode() return null when an entry is absent — never
 *     0.0. Calculators skip absent entries and emit no result row.
 *   - Table() never returns null; an unused condition yields an empty table.
 *
 * Frequencies in Hz. Mode numbers and nodal diameters are 0-based ints as
 * provided by the modal analysis (nd = 0 is the umbrella family).
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace LakeCore
{
    public class NodalFrequencyData : LakeComponent
    {
        public override string Name { get; set; } = "Nodal Diameters and Related Frequencies";

        public enum AnalysisCondition
        {
            Assembly,
            Redline
        }

        /// <summary>
        /// Sparse frequency lookup for one analysis condition:
        /// nodal diameter -> mode number -> frequency [Hz].
        /// </summary>
        public class FrequencyTable
        {
            private readonly SortedDictionary<int, SortedDictionary<int, double>> _data
                = new SortedDictionary<int, SortedDictionary<int, double>>();

            public bool IsEmpty
            {
                get { return _data.Count == 0; }
            }

            /// <summary>Nodal diameters present, ascending.</summary>
            public IEnumerable<int> NodalDiameters
            {
                get { return _data.Keys; }
            }

            /// <summary>Mode numbers present at a nodal diameter, ascending.
            /// Empty sequence if the diameter is absent.</summary>
            public IEnumerable<int> Modes(int nodalDiameter)
            {
                SortedDictionary<int, double> modes;
                if (_data.TryGetValue(nodalDiameter, out modes))
                    return modes.Keys;
                return Enumerable.Empty<int>();
            }

            /// <summary>Frequency at (nd, mode), or null if absent. Never 0.0
            /// as a placeholder.</summary>
            public double? Frequency(int nodalDiameter, int mode)
            {
                SortedDictionary<int, double> modes;
                double frequency;
                if (_data.TryGetValue(nodalDiameter, out modes) &&
                    modes.TryGetValue(mode, out frequency))
                {
                    return frequency;
                }
                return null;
            }

            /// <summary>Frequency of the lowest-numbered mode present at the
            /// nodal diameter, or null if the diameter has no modes.</summary>
            public double? FirstMode(int nodalDiameter)
            {
                SortedDictionary<int, double> modes;
                if (_data.TryGetValue(nodalDiameter, out modes) && modes.Count > 0)
                {
                    foreach (KeyValuePair<int, double> kv in modes)
                        return kv.Value;            // SortedDictionary: first = lowest mode
                }
                return null;
            }

            public void Set(int nodalDiameter, int mode, double frequency)
            {
                SortedDictionary<int, double> modes;
                if (!_data.TryGetValue(nodalDiameter, out modes))
                {
                    modes = new SortedDictionary<int, double>();
                    _data[nodalDiameter] = modes;
                }
                modes[mode] = frequency;
            }
        }

        private readonly Dictionary<AnalysisCondition, FrequencyTable> _tables
            = new Dictionary<AnalysisCondition, FrequencyTable>();

        /// <summary>Table for a condition. Never null — creates an empty
        /// table on first access.</summary>
        public FrequencyTable Table(AnalysisCondition condition)
        {
            FrequencyTable table;
            if (!_tables.TryGetValue(condition, out table))
            {
                table = new FrequencyTable();
                _tables[condition] = table;
            }
            return table;
        }

        /// <summary>Add or overwrite one frequency entry. Same signature as
        /// the original program's AddData.</summary>
        public void AddData(AnalysisCondition analysisCondition, int nodalDiameter, int mode, double frequency)
        {
            Table(analysisCondition).Set(nodalDiameter, mode, frequency);
        }

        /// <summary>Frequency at (condition, nd, mode) or null. Replaces the
        /// original float?-returning GetFrequency.</summary>
        public double? GetFrequency(AnalysisCondition analysisCondition, int nodalDiameter, int mode)
        {
            return Table(analysisCondition).Frequency(nodalDiameter, mode);
        }
    }
}
