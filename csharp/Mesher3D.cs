// =============================================================================
//  Mesher3D
//
//  3D wedge mesher. Revolves a Mesher2D mesh about the global Y axis into a
//  C3D15 sector for CalculiX cyclic-symmetric modal analysis.
//
//  Mesher3D is both the data carrier (Nodes / ElementsByRegion / NSets +
//  sector geometry) and the factory. Build with Revolve(), serialise with
//  WriteToInp().
//
//      var mesh3d = Mesher3D.Revolve(mesh2d, options);
//      mesh3d.WriteToInp(@"...\mesh3d.inp");
//
//  Layer scheme (matches revolve_mesh.py v2):
//      corner layer k, k = 0..N    : every 2D node copied at theta = k*S/N
//      vmid   layer k, k = 0..N-1  : only 2D corner-nodes copied at
//                                     theta = (k + 0.5)*S/N
//      => 2N + 1 angular layers total per 2D node
//
//  C3D15 orientation:
//      Face S1 (nodes 1-3) at corner layer k+1 (higher theta, more -Z)
//      Face S2 (nodes 4-6) at corner layer k   (lower theta)
//      Vertical midsides (nodes 13-15) at vmid layer k
//
//  Boundary nsets:
//      LEFT_SURF  = all nodes at corner layer 0     (theta = 0)
//      RIGHT_SURF = all nodes at corner layer N     (theta = S)
//      FIX_FACES  = master copies of fix-face 2D nodes -- includes all
//                   interior + RIGHT_SURF copies but NOT LEFT_SURF copies,
//                   so the SPC-on-FIX_FACES and cyclic MPC-on-LEFT/RIGHT
//                   constraint sets do not share dependent nodes (CalculiX
//                   forbids that combination).
//
//  Region propagation:
//      2D ELSET SEAL       -> 3D ELSET SEAL_ELEMS
//      2D ELSET BOND_COAT  -> 3D ELSET BOND_COAT_ELEMS
//      2D ELSET TOP_COAT   -> 3D ELSET TOP_COAT_ELEMS
//      (names configurable via MesherOptions)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LakeCore
{
    public sealed class Mesher3D
    {
        // ------------------ data carrier --------------------------------
        public Dictionary<int, double[]>         Nodes
            = new Dictionary<int, double[]>();   // nodeId -> (x, y, z)
        public Dictionary<string, List<int[]>>   ElementsByRegion
            = new Dictionary<string, List<int[]>>();  // region -> [eid, n1..n15]
        public Dictionary<string, HashSet<int>>  NSets
            = new Dictionary<string, HashSet<int>>();

        public double SectorAngleRad;
        public int    WedgesPerSector;

        // ------------------ public API ----------------------------------

        /// <summary>
        /// Revolve a 2D mesh into a 3D wedge sector. The 2D ELSET names are
        /// mapped to 3D ELSET names according to MesherOptions
        /// (SEAL -> SEAL_ELEMS, etc.). Any 2D ELSET not matching a known
        /// region name is preserved with an "_ELEMS" suffix.
        /// </summary>
        public static Mesher3D Revolve(Mesher2D mesh2d, MesherOptions options = null)
        {
            if (mesh2d == null)
                throw new ArgumentNullException(nameof(mesh2d));
            if (options == null) options = new MesherOptions();

            int N = options.WedgesPerSector;
            if (N < 1)
                throw new ArgumentException(
                    "WedgesPerSector must be >= 1", nameof(options));

            double sectorRad = options.SectorAngleDeg * Math.PI / 180.0;

            var nodes2d = mesh2d.Nodes;
            if (nodes2d.Count == 0)
                throw new InvalidOperationException(
                    "2D mesh has no nodes -- nothing to revolve");

            // Flatten 2D elements into (eid, conn, region) for uniform iteration.
            var flatElems = new List<(int eid, int[] conn, string region)>();
            foreach (var kv in mesh2d.ElementsByRegion)
                foreach (int[] e in kv.Value)
                    flatElems.Add((e[0], Slice(e, 1, 6), kv.Key));
            if (flatElems.Count == 0)
                throw new InvalidOperationException(
                    "2D mesh has no elements -- nothing to revolve");

            // Classify 2D nodes as corner-type (in slots 1-3 of any CAX6) vs
            // midside-type (slots 4-6). Overlap (rare) is treated as corner.
            var cornerNodes  = new HashSet<int>();
            var midsideNodes = new HashSet<int>();
            foreach (var ee in flatElems)
            {
                for (int j = 0; j < 3; j++) cornerNodes .Add(ee.conn[j]);
                for (int j = 3; j < 6; j++) midsideNodes.Add(ee.conn[j]);
            }
            midsideNodes.ExceptWith(cornerNodes);

            // FIX_FACES source: pick up whichever 2D nset name the upstream
            // pipeline used. Falls back to empty if no match.
            var fix2d = LookupNset(mesh2d.NSets,
                "RIGHTANDLEFT", "FIX_FACES", "FIX_TOP", "FIX_BOT");

            // ID-offset scheme: round-up the max 2D ID to the next power of
            // ten so each (layer, kind) pair occupies its own ID range.
            int max2dId = nodes2d.Keys.Max();
            long baseOff = (long)Math.Pow(10.0,
                              Math.Floor(Math.Log10(Math.Max(1, max2dId))) + 1);

            long CornerOff(int k) { return k * baseOff; }
            long VmidOff  (int k) { return (N + 1 + k) * baseOff; }

            double ThetaCorner(int k) { return k * sectorRad / N; }
            double ThetaVmid  (int k) { return (k + 0.5) * sectorRad / N; }

            // R_y * (x, y, 0) = (x*cos, y, -x*sin)
            double[] RotateY(double x, double y, double th)
            {
                double c = Math.Cos(th), s = Math.Sin(th);
                return new double[] { x * c, y, -x * s };
            }

            var nodes3d  = new Dictionary<int, double[]>(
                nodes2d.Count * (2 * N + 1));
            var leftSet  = new HashSet<int>();
            var rightSet = new HashSet<int>();
            var fixSet   = new HashSet<int>();

            // ---- corner layers (k = 0..N): every 2D node copied -----------
            for (int k = 0; k <= N; k++)
            {
                double th = ThetaCorner(k);
                long off  = CornerOff(k);
                foreach (var kv in nodes2d)
                {
                    int nid   = kv.Key;
                    double x  = kv.Value[0];
                    double y  = kv.Value[1];
                    int newId = checked((int)(nid + off));
                    nodes3d[newId] = RotateY(x, y, th);

                    if (k == 0) leftSet .Add(newId);
                    if (k == N) rightSet.Add(newId);

                    // FIX_FACES holds layer copies of fix 2D nodes EXCEPT at
                    // layer 0 (which is the cyclic-MPC slave -- CalculiX
                    // forbids a node being both an SPC dependent and an MPC
                    // dependent). Layer 0 inherits the SPC implicitly via the
                    // cyclic constraint  u_left = u_right * exp(i*phase) = 0.
                    if (k != 0 && fix2d.Contains(nid)) fixSet.Add(newId);
                }
            }

            // ---- vmid layers (k = 0..N-1): only 2D corner-nodes copied ----
            for (int k = 0; k < N; k++)
            {
                double th = ThetaVmid(k);
                long off  = VmidOff(k);
                foreach (int nid in cornerNodes)
                {
                    if (!nodes2d.TryGetValue(nid, out var xy)) continue;
                    int newId = checked((int)(nid + off));
                    nodes3d[newId] = RotateY(xy[0], xy[1], th);
                    if (fix2d.Contains(nid)) fixSet.Add(newId);
                }
            }

            // ---- C3D15 connectivity, per-region tags propagated -----------
            var elems3dByRegion = new Dictionary<string, List<int[]>>();
            int nextEid = flatElems.Max(e => e.eid) + 1;

            foreach (var ee in flatElems)
            {
                int c1 = ee.conn[0], c2 = ee.conn[1], c3 = ee.conn[2];
                int m12 = ee.conn[3], m23 = ee.conn[4], m31 = ee.conn[5];

                if (!elems3dByRegion.TryGetValue(ee.region, out var list))
                {
                    list = new List<int[]>();
                    elems3dByRegion[ee.region] = list;
                }

                for (int k = 0; k < N; k++)
                {
                    long offS1 = CornerOff(k + 1);  // higher theta
                    long offS2 = CornerOff(k);      // lower  theta
                    long offV  = VmidOff(k);

                    int b1  = checked((int)(c1  + offS1));
                    int b2  = checked((int)(c2  + offS1));
                    int b3  = checked((int)(c3  + offS1));
                    int t1  = checked((int)(c1  + offS2));
                    int t2  = checked((int)(c2  + offS2));
                    int t3  = checked((int)(c3  + offS2));
                    int b12 = checked((int)(m12 + offS1));
                    int b23 = checked((int)(m23 + offS1));
                    int b31 = checked((int)(m31 + offS1));
                    int t12 = checked((int)(m12 + offS2));
                    int t23 = checked((int)(m23 + offS2));
                    int t31 = checked((int)(m31 + offS2));
                    int v1  = checked((int)(c1  + offV ));
                    int v2  = checked((int)(c2  + offV ));
                    int v3  = checked((int)(c3  + offV ));

                    int eid = (k == 0) ? ee.eid : nextEid++;
                    list.Add(new int[] {
                        eid,
                        b1, b2, b3,
                        t1, t2, t3,
                        b12, b23, b31,
                        t12, t23, t31,
                        v1, v2, v3
                    });
                }
            }

            // Translate region names (SEAL -> SEAL_ELEMS etc.).
            var elemsOut = new Dictionary<string, List<int[]>>();
            foreach (var kv in elems3dByRegion)
            {
                string outName = Map2DRegionTo3DName(kv.Key, options);
                elemsOut[outName] = kv.Value;
            }

            return new Mesher3D
            {
                Nodes = nodes3d,
                ElementsByRegion = elemsOut,
                NSets = new Dictionary<string, HashSet<int>>
                {
                    [options.LeftSurfNset ] = leftSet,
                    [options.RightSurfNset] = rightSet,
                    [options.FixFacesNset ] = fixSet,
                },
                SectorAngleRad = sectorRad,
                WedgesPerSector = N,
            };
        }

        public void WriteToInp(string path)
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("** Mesher3D wedge mesh");
                sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "** Sector angle (rad): {0:R}",  SectorAngleRad));
                sw.WriteLine("** Wedges/sector  : " + WedgesPerSector);
                sw.WriteLine("** ----------------------------------------------");

                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in Nodes.OrderBy(k => k.Key))
                {
                    double[] xyz = kv.Value;
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, {3:R}",
                        kv.Key, xyz[0], xyz[1], xyz[2]));
                }

                foreach (var kv in ElementsByRegion)
                {
                    sw.WriteLine("*ELEMENT, TYPE=C3D15, ELSET=" + kv.Key);
                    foreach (int[] e in kv.Value) WriteElement(sw, e);
                }

                foreach (var kv in NSets)
                {
                    sw.WriteLine("*NSET, NSET=" + kv.Key);
                    WriteIdList(sw, kv.Value.OrderBy(x => x));
                }
            }
        }

        // ====================================================================
        //  helpers
        // ====================================================================

        private static string Map2DRegionTo3DName(string in2D, MesherOptions opts)
        {
            if (string.Equals(in2D, opts.SealGroupName, StringComparison.OrdinalIgnoreCase))
                return opts.SealElset3D;
            if (string.Equals(in2D, opts.BondGroupName, StringComparison.OrdinalIgnoreCase))
                return opts.BondElset3D;
            if (string.Equals(in2D, opts.TopGroupName,  StringComparison.OrdinalIgnoreCase))
                return opts.TopElset3D;
            return in2D + "_ELEMS";
        }

        private static HashSet<int> LookupNset(
            Dictionary<string, HashSet<int>> nsets, params string[] names)
        {
            foreach (string n in names)
                foreach (var kv in nsets)
                    if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
            return new HashSet<int>();
        }

        private static int[] Slice(int[] a, int from, int count)
        {
            int[] r = new int[count];
            Array.Copy(a, from, r, 0, count);
            return r;
        }

        private static void WriteElement(StreamWriter sw, int[] e)
        {
            sw.Write(e[0]);
            for (int j = 1; j < e.Length; j++)
            {
                sw.Write(",");
                sw.Write(e[j]);
            }
            sw.WriteLine();
        }

        private static void WriteIdList(StreamWriter sw, IEnumerable<int> ids)
        {
            var buf = new List<int>(); int col = 0;
            foreach (int id in ids)
            {
                buf.Add(id); col++;
                if (col == 8)
                {
                    sw.WriteLine(string.Join(", ", buf));
                    buf.Clear(); col = 0;
                }
            }
            if (buf.Count > 0) sw.WriteLine(string.Join(", ", buf));
        }
    }
}
