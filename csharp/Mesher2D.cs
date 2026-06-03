// =============================================================================
//  Mesher2D
//
//  2D axisymmetric mesher. Takes the OCC Fragment compound produced by
//  TopoHelpers.BuildCoatedSeal (or a bare seal face wrapped in a compound)
//  and produces a CAX6 mesh with one ELSET per material region:
//      SEAL                  (always)
//      BOND_COAT             (only when isCoated)
//      TOP_COAT              (only when isCoated)
//  All three reference a single shared node table -- the mesh is conformal
//  across region interfaces.
//
//  Mesher2D is both the data carrier (Nodes / ElementsByRegion / NSets) and
//  the factory. Build with Generate(), serialise with WriteToInp().
//
//      var mesh2d = Mesher2D.Generate(compound, isCoated, bond, top, opts);
//      mesh2d.WriteToInp(@"...\dump.inp");
//
//  Internal pipeline:
//      1. Export the compound to a transient STEP file.
//      2. Initialise gmsh, ImportShapes, RemoveAllDuplicates, Synchronize.
//      3. Classify each imported face by centroid against the bond / top
//         polygons (innermost-wins), tag physical groups.
//      4. Apply adaptive transfinite tip discretisation on the substrate's
//         outer-most edges so the angular count there is deterministic.
//      5. Generate the mesh, write to a temp .inp.
//      6. Rewrite that .inp: rename gmsh's Surface<N> ELSETs to the
//         physical-group names by matching nset node-sets, and replace the
//         CPS6 element type with CAX6.
//      7. Parse the rewritten file into the in-memory representation.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
// Adjust if your project uses a different namespace:
using GmshNet;

namespace LakeCore
{
    public sealed class Mesher2D
    {
        // ------------------ data carrier --------------------------------
        public Dictionary<int, double[]>         Nodes
            = new Dictionary<int, double[]>();   // nodeId -> (x, y)
        public Dictionary<string, List<int[]>>   ElementsByRegion
            = new Dictionary<string, List<int[]>>();  // region -> [eid, n1..n6]
        public Dictionary<string, HashSet<int>>  NSets
            = new Dictionary<string, HashSet<int>>();

        // ------------------ public API ----------------------------------

        /// <summary>
        /// Mesh the OCC compound and return the 2D mesh in-memory.
        /// For coated geometries, pass isCoated=true and the original
        /// (un-inflated) bond/top polygons; for bare seals, isCoated=false
        /// and both polygons may be null.
        /// </summary>
        public static Mesher2D Generate(
            TopoDS_Compound  fragmentedCompound,
            bool             isCoated,
            List<gp_Pnt>     bondPolygon,
            List<gp_Pnt>     topPolygon,
            MesherOptions    options = null)
        {
            if (options == null) options = new MesherOptions();

            string tmpStep = Path.Combine(Path.GetTempPath(),
                $"mesher2d_{Guid.NewGuid():N}.step");
            string tmpInp  = Path.Combine(Path.GetTempPath(),
                $"mesher2d_{Guid.NewGuid():N}.inp");
            try
            {
                ExportCompoundAsStep(fragmentedCompound, tmpStep);
                InvokeGmsh(tmpStep, isCoated, bondPolygon, topPolygon,
                           options, tmpInp);
                return ParseInp(tmpInp);
            }
            finally
            {
                try { if (File.Exists(tmpStep)) File.Delete(tmpStep); } catch { }
                try { if (File.Exists(tmpInp )) File.Delete(tmpInp ); } catch { }
            }
        }

        public void WriteToInp(string path)
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("** Mesher2D output");
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in Nodes.OrderBy(k => k.Key))
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, 0.0",
                        kv.Key, kv.Value[0], kv.Value[1]));
                }
                foreach (var kv in ElementsByRegion)
                {
                    sw.WriteLine("*ELEMENT, TYPE=CAX6, ELSET=" + kv.Key);
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
        //  STEP EXPORT
        // ====================================================================

        private static void ExportCompoundAsStep(TopoDS_Shape shape, string path)
        {
            Interface_Static.SetIVal("write.surfacecurve.mode", 1);
            Interface_Static.SetIVal("write.step.assembly",    0);

            var writer  = new STEPControl_Writer();
            var xferRes = writer.Transfer(shape,
                STEPControl_StepModelType.STEPControl_AsIs);
            if (xferRes != IFSelect_ReturnStatus.IFSelect_RetDone)
                throw new InvalidOperationException(
                    "STEPControl_Writer.Transfer failed: " + xferRes);

            var writeRes = writer.Write(path);
            if (writeRes != IFSelect_ReturnStatus.IFSelect_RetDone)
                throw new InvalidOperationException(
                    "STEPControl_Writer.Write failed: " + writeRes);
        }

        // ====================================================================
        //  GMSH IMPORT + CLASSIFY + MESH
        // ====================================================================

        private static void InvokeGmsh(
            string         stepPath,
            bool           isCoated,
            List<gp_Pnt>   bondPoly,
            List<gp_Pnt>   topPoly,
            MesherOptions  opts,
            string         outInpPath)
        {
            // gmsh's writer infers format from file extension; .inp is Abaqus.
            string gmshOut = outInpPath + ".gmsh.tmp.inp";

            Gmsh.Initialize();
            try
            {
                Gmsh.Option.SetNumber("General.Terminal",
                                      opts.QuietGmsh ? 0 : 1);
                Gmsh.Option.SetNumber("Mesh.Algorithm",       opts.MeshAlgorithm);
                Gmsh.Option.SetNumber("Mesh.ElementOrder",    2);
                Gmsh.Option.SetNumber("Mesh.SecondOrderIncomplete", 0);
                Gmsh.Option.SetNumber("Mesh.MeshSizeMin",
                                      isCoated ? opts.CoatingMeshSize
                                               : opts.MeshSize);
                Gmsh.Option.SetNumber("Mesh.MeshSizeMax",     opts.MeshSize);
                Gmsh.Option.SetNumber("Mesh.SaveAll",         0);
                Gmsh.Option.SetNumber("Mesh.SaveGroupsOfNodes", 1);
                Gmsh.Option.SetNumber("Mesh.Smoothing",       opts.Smoothing);
                Gmsh.Option.SetNumber("Mesh.Optimize",        opts.OptimizeMesh ? 1 : 0);
                Gmsh.Option.SetNumber("Mesh.OptimizeNetgen",  opts.OptimizeMesh ? 1 : 0);

                _ = Gmsh.Model.Occ.ImportShapes(stepPath, false, "");

                // RemoveAllDuplicates uses BOP fragments internally and the
                // BOP engine errors with "boolean fragments failed" when
                // there is only one input shape (nothing to fuse). For the
                // bare-seal case the substrate is a single TopoDS_Face, so
                // there's nothing to deduplicate -- skip the call.
                // Synchronize first so we can use the synced GetEntities,
                // then sync again after RemoveAllDuplicates rebuilds the
                // OCC kernel topology.
                Gmsh.Model.Occ.Synchronize();
                if (Gmsh.Model.GetEntities(2).Length > 1)
                {
                    Gmsh.Model.Occ.RemoveAllDuplicates();
                    Gmsh.Model.Occ.Synchronize();
                }

                // Auto-tag the seal's two mounting edges as physical curves
                // FIX_TOP / FIX_BOT (configurable names). The bounding-box
                // heuristic is geometry-agnostic: any edge that lies flat on
                // the model's global yMin or yMax line is a mount face,
                // regardless of which TaperedToothBuilder / KnifeBuilder /
                // etc. produced the cross-section.
                TagAxialExtremeEdges(opts);

                (int, int)[] faceDimTags = Gmsh.Model.GetEntities(2);

                var sealTags = new List<int>();
                var bondTags = new List<int>();
                var topTags  = new List<int>();

                foreach (var dimTag in faceDimTags)
                {
                    int dim = dimTag.Item1;
                    int tag = dimTag.Item2;
                    if (dim != 2) continue;
                    double cx, cy, cz;
                    Gmsh.Model.Occ.GetCenterOfMass(2, tag, out cx, out cy, out cz);

                    if (isCoated)
                    {
                        bool inBond = bondPoly != null
                                      && PointInPolygon2D(cx, cy, bondPoly);
                        bool inTop  = topPoly  != null
                                      && PointInPolygon2D(cx, cy, topPoly);
                        if      (inBond) bondTags.Add(tag);
                        else if (inTop)  topTags .Add(tag);
                        else             sealTags.Add(tag);
                    }
                    else
                    {
                        sealTags.Add(tag);
                    }
                }

                AddPhysicalSurface(sealTags, opts.SealGroupName);
                if (isCoated)
                {
                    AddPhysicalSurface(bondTags, opts.BondGroupName);
                    AddPhysicalSurface(topTags,  opts.TopGroupName);
                }

                if (isCoated && (bondTags.Count + topTags.Count) > 0)
                {
                    var coatDimTags = bondTags.Concat(topTags)
                                              .Select(t => (2, t))
                                              .ToArray();
                    Gmsh.Model.Mesh.SetSize(coatDimTags, opts.CoatingMeshSize);
                }

                if (opts.UseTransfiniteTip)
                    ApplyTransfiniteTip(sealTags, opts);

                Gmsh.Model.Mesh.Generate(2);
                Gmsh.Write(gmshOut);

                RewriteGmshInp(gmshOut, outInpPath);
            }
            finally
            {
                try { if (File.Exists(gmshOut)) File.Delete(gmshOut); } catch { }
                Gmsh.Finalize();
            }
        }

        private static void AddPhysicalSurface(List<int> faceTags, string name)
        {
            if (faceTags == null || faceTags.Count == 0) return;
            int phys = Gmsh.Model.AddPhysicalGroup(2, faceTags.ToArray(), -1);
            Gmsh.Model.SetPhysicalName(2, phys, name);
        }

        private static bool PointInPolygon2D(double px, double py, List<gp_Pnt> poly)
        {
            int n = poly.Count, crossings = 0;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];
                if ((a.Y > py) != (b.Y > py))
                {
                    double xCross = a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (px < xCross) crossings++;
                }
            }
            return (crossings & 1) == 1;
        }

        // ====================================================================
        //  AUTO-TAG MOUNT EDGES (FIX_TOP / FIX_BOT) VIA BBOX
        // ====================================================================

        // Find the substrate's axial-extreme edges -- those that lie flat on
        // the model's global yMin or yMax line -- and tag them as physical
        // curves so they survive gmsh's INP writer as NSETs. The check
        // "edge's own yMin AND yMax both equal global yMax" matches only
        // edges that lie *along* the extreme; vertical edges that merely
        // touch a corner at yMax are excluded by construction.
        private static void TagAxialExtremeEdges(MesherOptions opts)
        {
            double tol = opts.BoundingBoxTolerance;

            double gXmin, gYmin, gZmin, gXmax, gYmax, gZmax;
            Gmsh.Model.GetBoundingBox(-1, -1,
                out gXmin, out gYmin, out gZmin,
                out gXmax, out gYmax, out gZmax);

            var topEdges = new List<int>();
            var botEdges = new List<int>();

            (int, int)[] edges = Gmsh.Model.GetEntities(1);
            foreach (var dimTag in edges)
            {
                int eTag = dimTag.Item2;
                double eXmin, eYmin, eZmin, eXmax, eYmax, eZmax;
                Gmsh.Model.GetBoundingBox(1, eTag,
                    out eXmin, out eYmin, out eZmin,
                    out eXmax, out eYmax, out eZmax);

                bool flatOnTop = Math.Abs(eYmax - gYmax) < tol
                              && Math.Abs(eYmin - gYmax) < tol;
                bool flatOnBot = Math.Abs(eYmax - gYmin) < tol
                              && Math.Abs(eYmin - gYmin) < tol;

                if      (flatOnTop) topEdges.Add(eTag);
                else if (flatOnBot) botEdges.Add(eTag);
            }

            AddPhysicalCurve(topEdges, opts.FixTopCurveName);
            AddPhysicalCurve(botEdges, opts.FixBotCurveName);
        }

        private static void AddPhysicalCurve(List<int> edgeTags, string name)
        {
            if (edgeTags == null || edgeTags.Count == 0) return;
            if (string.IsNullOrEmpty(name)) return;
            int phys = Gmsh.Model.AddPhysicalGroup(1, edgeTags.ToArray(), -1);
            Gmsh.Model.SetPhysicalName(1, phys, name);
        }

        // ====================================================================
        //  ADAPTIVE TRANSFINITE TIP
        // ====================================================================

        // Identify the substrate's outer tip edges -- the ones whose midpoint
        // X is within a band of the global max -- and fix their node count
        // via SetTransfiniteCurve. Mirrors APDL's LESIZE pattern.
        private static void ApplyTransfiniteTip(List<int> sealFaceTags,
                                                MesherOptions opts)
        {
            if (sealFaceTags == null || sealFaceTags.Count == 0) return;

            var edgeMaxX = new Dictionary<int, double>();
            var edgeLen  = new Dictionary<int, double>();

            foreach (int faceTag in sealFaceTags)
            {
                var faceDimTags = new (int, int)[] { (2, faceTag) };
                (int, int)[] edges = Gmsh.Model.GetBoundary(
                    faceDimTags, false, false, false);

                foreach (var e in edges)
                {
                    int eDim = e.Item1; int eTag = Math.Abs(e.Item2);
                    if (eDim != 1) continue;
                    if (edgeMaxX.ContainsKey(eTag)) continue;

                    double cx, cy, cz;
                    Gmsh.Model.Occ.GetCenterOfMass(1, eTag,
                                                   out cx, out cy, out cz);
                    edgeMaxX[eTag] = cx;
                    edgeLen [eTag] = ApproxEdgeLength(eTag);
                }
            }

            if (edgeMaxX.Count == 0) return;

            double globalMaxX = edgeMaxX.Values.Max();
            double band = Math.Max(opts.TipNodeSpacing,
                                   Math.Abs(globalMaxX) * 0.01);

            foreach (var kv in edgeMaxX)
            {
                int    eTag = kv.Key;
                double midX = kv.Value;
                if (midX < globalMaxX - band) continue;

                double L = edgeLen.TryGetValue(eTag, out var l) ? l : 0.0;
                if (L <= 0.0) continue;

                int nodes = (int)Math.Round(L / opts.TipNodeSpacing) + 1;
                if (nodes < opts.MinTipNodes) nodes = opts.MinTipNodes;
                if (nodes > opts.MaxTipNodes) nodes = opts.MaxTipNodes;

                Gmsh.Model.Mesh.SetTransfiniteCurve(eTag, nodes,
                                                    "Progression", 1.0);
            }
        }

        private static double ApproxEdgeLength(int edgeTag)
        {
            try
            {
                // GetMass returns the mass property (= length for 1D entities
                // with unit density); the wrapper returns it directly.
                double mass = Gmsh.Model.Occ.GetMass(1, edgeTag);
                return Math.Abs(mass);
            }
            catch
            {
                return 0.0;
            }
        }

        // ====================================================================
        //  INP REWRITE  (CPS6 -> CAX6, Surface<N> -> physical name)
        // ====================================================================

        private static void RewriteGmshInp(string srcPath, string dstPath)
        {
            var nodes        = new Dictionary<int, double[]>();
            var elemsByElset = new Dictionary<string, List<int[]>>();
            var nsetByName   = new Dictionary<string, HashSet<int>>();
            var elsetOrder   = new List<string>();
            var nsetOrder    = new List<string>();

            ParseInpStreaming(srcPath, nodes, elemsByElset, elsetOrder,
                              nsetByName, nsetOrder);

            // For each gmsh-named ELSET, find the NSET that holds exactly the
            // same node IDs and adopt its name.
            var elsetRename = new Dictionary<string, string>();
            foreach (string elsetName in elsetOrder)
            {
                var nodesInElset = NodesOfElset(elemsByElset[elsetName]);
                string matched = null;
                foreach (string nsetName in nsetOrder)
                {
                    if (nodesInElset.SetEquals(nsetByName[nsetName]))
                    {
                        matched = nsetName;
                        break;
                    }
                }
                elsetRename[elsetName] = matched ?? elsetName;
            }

            using (var sw = new StreamWriter(dstPath))
            {
                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in nodes.OrderBy(k => k.Key))
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, 0.0",
                        kv.Key, kv.Value[0], kv.Value[1]));
                }

                foreach (string elsetName in elsetOrder)
                {
                    sw.WriteLine("*ELEMENT, TYPE=CAX6, ELSET="
                                 + elsetRename[elsetName]);
                    foreach (int[] e in elemsByElset[elsetName])
                        WriteElement(sw, e);
                }

                foreach (string nsetName in nsetOrder)
                {
                    sw.WriteLine("*NSET, NSET=" + nsetName);
                    WriteIdList(sw, nsetByName[nsetName].OrderBy(x => x));
                }
            }
        }

        private static HashSet<int> NodesOfElset(List<int[]> elems)
        {
            var s = new HashSet<int>();
            foreach (int[] e in elems)
                for (int j = 1; j < e.Length; j++) s.Add(e[j]);
            return s;
        }

        // ====================================================================
        //  INP PARSER  (used by Generate() and RewriteGmshInp())
        // ====================================================================

        private static Mesher2D ParseInp(string path)
        {
            var nodes  = new Dictionary<int, double[]>();
            var elsets = new Dictionary<string, List<int[]>>();
            var nsets  = new Dictionary<string, HashSet<int>>();
            var elsetOrder = new List<string>();
            var nsetOrder  = new List<string>();
            ParseInpStreaming(path, nodes, elsets, elsetOrder, nsets, nsetOrder);

            // Data invariant for Mesher2D: every CAX6 triangle is CCW in
            // the XY plane (signed area > 0). Some mesh sources -- gmsh in
            // particular, depending on the source face's surface normal --
            // emit triangles in CW order; downstream Mesher3D.Revolve
            // assumes CCW (otherwise every revolved C3D15 has a negative
            // jacobian determinant in CalculiX). Flip in-place where needed.
            EnforceCcwOrientation(nodes, elsets);

            return new Mesher2D {
                Nodes = nodes,
                ElementsByRegion = elsets,
                NSets = nsets,
            };
        }

        // For each CAX6 element, compute the signed area of its corner
        // triangle (e[1], e[2], e[3]) in XY. If signed area < 0 (CW), swap
        // c2<->c3 and the matching midsides m12<->m31 so the element
        // becomes CCW while preserving the same topology.
        private static void EnforceCcwOrientation(
            Dictionary<int, double[]>        nodes,
            Dictionary<string, List<int[]>>  elsetsByRegion)
        {
            foreach (var kv in elsetsByRegion)
            {
                foreach (int[] e in kv.Value)
                {
                    if (e.Length < 7) continue;
                    double[] p1 = nodes[e[1]];
                    double[] p2 = nodes[e[2]];
                    double[] p3 = nodes[e[3]];
                    double signedArea = 0.5 * (
                        (p2[0] - p1[0]) * (p3[1] - p1[1]) -
                        (p3[0] - p1[0]) * (p2[1] - p1[1]));
                    if (signedArea < 0.0)
                    {
                        // swap c2 <-> c3
                        int tc = e[2]; e[2] = e[3]; e[3] = tc;
                        // swap m12 <-> m31  (midsides 4 and 6 in CAX6 indexing)
                        int tm = e[4]; e[4] = e[6]; e[6] = tm;
                    }
                }
            }
        }

        private static void ParseInpStreaming(
            string                                    path,
            Dictionary<int, double[]>                 nodes,
            Dictionary<string, List<int[]>>           elemsByElset,
            List<string>                              elsetOrder,
            Dictionary<string, HashSet<int>>          nsetByName,
            List<string>                              nsetOrder)
        {
            string section  = null;
            string sectName = null;
            int    elemNodeCount = 0;

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("**")) continue;
                if (line.StartsWith("--")) continue;  // gmsh footer divider

                if (line.StartsWith("*"))
                {
                    string head = SplitFirst(line).ToLowerInvariant();
                    sectName = null;
                    if (head == "*node")
                    {
                        section = "node";
                    }
                    else if (head == "*element")
                    {
                        section = "elem";
                        string typeTok = FindAssignment(line, "type");
                        elemNodeCount = IsQuadTri6(typeTok) ? 6 : 0;
                        sectName = FindAssignment(line, "elset");
                        if (sectName != null && elemNodeCount == 6
                            && !elemsByElset.ContainsKey(sectName))
                        {
                            elemsByElset[sectName] = new List<int[]>();
                            elsetOrder.Add(sectName);
                        }
                    }
                    else if (head == "*nset")
                    {
                        section = "nset";
                        sectName = FindAssignment(line, "nset");
                        if (sectName != null
                            && !nsetByName.ContainsKey(sectName))
                        {
                            nsetByName[sectName] = new HashSet<int>();
                            nsetOrder.Add(sectName);
                        }
                    }
                    else
                    {
                        section = null;
                    }
                    continue;
                }

                if (section == "node")
                {
                    string[] parts = SplitCsv(line);
                    if (parts.Length >= 3)
                    {
                        int nid = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        double x = double.Parse(parts[1], CultureInfo.InvariantCulture);
                        double y = double.Parse(parts[2], CultureInfo.InvariantCulture);
                        nodes[nid] = new double[] { x, y };
                    }
                }
                else if (section == "elem" && sectName != null
                                            && elemNodeCount == 6)
                {
                    string[] parts = SplitCsv(line);
                    if (parts.Length >= 1 + elemNodeCount)
                    {
                        int[] e = new int[1 + elemNodeCount];
                        for (int j = 0; j < 1 + elemNodeCount; j++)
                            e[j] = int.Parse(parts[j], CultureInfo.InvariantCulture);
                        elemsByElset[sectName].Add(e);
                    }
                }
                else if (section == "nset" && sectName != null)
                {
                    string[] parts = SplitCsv(line);
                    foreach (string p in parts)
                    {
                        if (p.Length == 0) continue;
                        nsetByName[sectName].Add(
                            int.Parse(p, CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        private static string FindAssignment(string line, string key)
        {
            foreach (string raw in line.Split(','))
            {
                string tok = raw.Trim();
                int eq = tok.IndexOf('=');
                if (eq < 0) continue;
                string k = tok.Substring(0, eq).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return tok.Substring(eq + 1).Trim();
            }
            return null;
        }

        private static bool IsQuadTri6(string typeTok)
        {
            if (string.IsNullOrEmpty(typeTok)) return false;
            string t = typeTok.ToUpperInvariant();
            return t == "CAX6" || t == "CPS6" || t == "CPE6" || t == "T6"
                || t == "STRI65" || t == "S6";
        }

        private static string SplitFirst(string line)
        {
            int comma = line.IndexOf(',');
            return (comma < 0 ? line : line.Substring(0, comma)).Trim();
        }

        private static string[] SplitCsv(string line)
        {
            string[] raw = line.Split(',');
            for (int i = 0; i < raw.Length; i++) raw[i] = raw[i].Trim();
            return raw;
        }

        // ====================================================================
        //  WRITERS  (instance WriteToInp + INP rewrite share these)
        // ====================================================================

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
