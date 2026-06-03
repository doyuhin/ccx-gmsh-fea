// =============================================================================
//  ModalResults
//
//  Parsed eigenvalue table from a CalculiX *FREQUENCY step's .dat file. The
//  table has one row per (nodal-diameter, mode-number) pair; each row carries
//  the eigenvalue, real and imaginary parts of the angular frequency, and
//  the cyclic frequency in Hz.
//
//  CalculiX reports each cyclic-symmetric physical mode twice (the two halves
//  of the complex eigenvector). For convenience, PhysicalModesByND() removes
//  the consecutive duplicates and returns the unique frequencies per ND.
//
//  Usage:
//      var results = ModalResults.ParseDat(@"...\disc_modal.dat");
//      double f_nd2_mode1 = results.PhysicalModesByND()[2][0];
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LakeCore
{
    public class ModalResults
    {
        public class EigenEntry
        {
            public int    NodalDiameter;
            public int    ModeNumber;
            public double EigenvalueSquared;     // (omega^2) in (rad/s)^2
            public double OmegaRealRadPerSec;
            public double FrequencyHz;           // OmegaReal / (2*pi)
            public double OmegaImagRadPerSec;    // 0 for undamped
        }

        public List<EigenEntry> Entries = new List<EigenEntry>();

        /// <summary>
        /// Returns, per nodal diameter, the unique physical-mode frequencies
        /// in Hz (ordered by mode index). CCX writes each cyclic mode twice
        /// for the complex-eigenvector real/imag halves; this collapses the
        /// consecutive duplicates so length(list) = number of physical modes.
        /// </summary>
        public Dictionary<int, List<double>> PhysicalModesByND()
        {
            // Group entries by ND, preserving CCX's order within each.
            var byNd = new Dictionary<int, List<double>>();
            foreach (var e in Entries)
            {
                List<double> list;
                if (!byNd.TryGetValue(e.NodalDiameter, out list))
                {
                    list = new List<double>();
                    byNd[e.NodalDiameter] = list;
                }
                list.Add(e.FrequencyHz);
            }

            // Collapse consecutive duplicates (CCX's paired modes).
            var result = new Dictionary<int, List<double>>();
            foreach (var kv in byNd)
            {
                var unique = new List<double>();
                double prev = double.NaN;
                foreach (double f in kv.Value)
                {
                    if (double.IsNaN(prev) ||
                        Math.Abs(f - prev) > 1e-4 * Math.Max(1.0, Math.Abs(f)))
                    {
                        unique.Add(f);
                    }
                    prev = f;
                }
                result[kv.Key] = unique;
            }
            return result;
        }

        // ====================================================================
        //  PARSER
        // ====================================================================

        public static ModalResults ParseDat(string datPath)
        {
            if (string.IsNullOrEmpty(datPath))
                throw new ArgumentNullException(nameof(datPath));
            if (!File.Exists(datPath))
                throw new FileNotFoundException("CalculiX .dat file not found",
                                                datPath);

            var results = new ModalResults();

            // We don't bother with section detection. CCX writes its section
            // headers with spaced letters ("E I G E N V A L U E   O U T P U T")
            // which are version-dependent and easy to mis-match. Instead we
            // try to parse every line as an eigenvalue row:
            //     ND(int)  MODE(int)  EIGVAL(float)  OMEGA_R(float)  FREQ_HZ(float)  OMEGA_I(float)
            // PARTICIPATION FACTORS and EFFECTIVE MASS rows have only ONE
            // integer (MODE_NO) followed by floats, so they fail the "second
            // column is an int" check and get skipped naturally.

            foreach (string raw in File.ReadLines(datPath))
            {
                if (raw == null) continue;
                string[] parts = raw.Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;

                int nd, mode;
                double eigval, omegaReal, freqHz, omegaImag;
                if (!int.TryParse(parts[0], NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out nd)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out mode)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out eigval)) continue;
                if (!double.TryParse(parts[3], NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out omegaReal)) continue;
                if (!double.TryParse(parts[4], NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out freqHz)) continue;
                if (!double.TryParse(parts[5], NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out omegaImag)) continue;

                results.Entries.Add(new EigenEntry {
                    NodalDiameter      = nd,
                    ModeNumber         = mode,
                    EigenvalueSquared  = eigval,
                    OmegaRealRadPerSec = omegaReal,
                    FrequencyHz        = freqHz,
                    OmegaImagRadPerSec = omegaImag,
                });
            }

            return results;
        }
    }
}
