// =============================================================================
//  Mesher
//
//  Unified 2D + 3D mesh generator for the cyclic-modal labyrinth-seal pipeline.
//  Replaces:
//      - GmshCoatedSealMesher (C#)   --  the 2D-only PrePoMax/gmsh wrapper
//      - revolve_mesh.py     (Python) --  the 2D->3D wedge revolver
//
//  Pipeline run by MeshAndRevolve():
//      1. Export the OCC compound (from TopoHelpers.BuildCoatedSeal or a bare
//         seal face) to a temporary STEP file.
//      2. Initialise gmsh, import the STEP, RemoveAllDuplicates to re-fuse any
//         shared topology dropped by the STEP roundtrip, Synchronize.
//      3. Classify each imported face by centroid against the bond/top polygons
//         (innermost-wins) and tag physical groups SEAL / BOND_COAT / TOP_COAT.
//         In the bare case only SEAL is created.
//      4. Apply adaptive transfinite discretization on the substrate tip edges
//         so the angular count there is deterministic and geometry-aware.
//      5. Generate the 2D quadratic-triangle mesh and write to a temp .inp.
//      6. Parse the temp .inp into an in-memory Mesh2D (renaming ELSETs from
//         gmsh's Surface<N> to the physical-group names by matching nsets).
//      7. Optionally save a copy of the 2D mesh to the user-supplied dump path.
//      8. Revolve the Mesh2D into a 3D wedge mesh (Mesh3D) about the global Y
//         axis: N stacked C3D15 wedges per sector with 2N+1 angular node
//         layers, LEFT_SURF / RIGHT_SURF nsets for cyclic-MPC pairing,
//         FIX_FACES nset for boundary conditions, per-region 3D elsets.
//      9. WriteToInp(Mesh3D, path) writes the final CalculiX deck input.
//
//  The Mesher is stateless; all per-run configuration lives in Options.
//
//  Notes:
//    - Gmsh.Net (noy1993) wrapper assumptions match those in the previous
//      mesher: return-style ImportShapes / GetEntities / Fragment; out-style
//      GetCenterOfMass; 3-arg AddPhysicalGroup followed by SetPhysicalName.
//    - C# 7.3 compatible (no ??=, no using-var declarations, no records).
//    - Native gmsh-4.X.dll must sit alongside the executable at run time.
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
    public static class Mesher
    {
        // ====================================================================
        //  OPTIONS
        // ====================================================================

        public sealed class Options
        {
            // -------- 2D mesher sizing -----------------------------------
            public double MeshSize            = 0.5;   // substrate / bulk
            public double CoatingMeshSize     = 0.15;  // bond + top coats
            public int    MeshAlgorithm       = 6;     // 6 = Frontal-Delaunay
            public bool   QuietGmsh           = true;

            // -------- 2D quality (matches APDL SMRTSIZE,,,4,3) -----------
            public int    Smoothing           = 3;
            public bool   OptimizeMesh        = true;

            // -------- Adaptive transfinite tip discretization -----------
            // Mirrors APDL's lineDivideNumber = 18/meshSize -- specify a
            // target spacing along the substrate tip edge, bounded by
            // [MinTipNodes, MaxTipNodes]. Set UseTransfiniteTip = false to
            // let the mesher size the tip from MeshSize/CoatingMeshSize.
            public bool   UseTransfiniteTip   = true;
            public double TipNodeSpacing      = 0.05;  // mm
            public int    MinTipNodes         = 5;
            public int    MaxTipNodes         = 30;

            // -------- 3D revolve -----------------------------------------
            public double SectorAngleDeg      = 15.0;
            public int    WedgesPerSector     = 5;

            // -------- 2D physical-group names ----------------------------
            public string SealGroupName       = "SEAL";
            public string BondGroupName       = "BOND_COAT";
            public string TopGroupName        = "TOP_COAT";

            // -------- 3D output naming -----------------------------------
            public string SealElset3D         = "SEAL_ELEMS";
            public string BondElset3D         = "BOND_COAT_ELEMS";
            public string TopElset3D          = "TOP_COAT_ELEMS";
            public string LeftSurfNset        = "LEFT_SURF";
            public string RightSurfNset       = "RIGHT_SURF";
            public string FixFacesNset        = "FIX_FACES";

            // -------- Debug ----------------------------------------------
            // If non-null, the 2D mesh is also written to this path before
            // the revolve runs. Useful for inspecting the intermediate.
            public string Dump2DMeshTo        = null;
        }

        // ====================================================================
        //  IN-MEMORY MESH TYPES
        // ====================================================================

        public sealed class Mesh2D
        {
            // nodeId -> (x, y)
            public Dictionary<int, double[]> Nodes
                = new Dictionary<int, double[]>();

            // region name -> list of (elementId, [n1..n6]) for CAX6
            public Dictionary<string, List<int[]>> ElementsByRegion
                = new Dictionary<string, List<int[]>>();

            // nset name -> set of node ids
            public Dictionary<string, HashSet<int>> NSets
                = new Dictionary<string, HashSet<int>>();
        }

        public sealed class Mesh3D
        {
            // nodeId -> (x, y, z)
            public Dictionary<int, double[]> Nodes
                = new Dictionary<int, double[]>();

            // region name -> list of (elementId, [n1..n15]) for C3D15
            public Dictionary<string, List<int[]>> ElementsByRegion
                = new Dictionary<string, List<int[]>>();

            // nset name -> set of node ids
            public Dictionary<string, HashSet<int>> NSets
                = new Dictionary<string, HashSet<int>>();

            public double SectorAngleRad;
            public int    WedgesPerSector;
        }

        // ====================================================================
        //  PUBLIC API
        // ====================================================================

        /// <summary>
        /// Mesh the geometry in 2D and revolve into 3D wedges in one call.
        /// Returns the 3D mesh in-memory. For coated geometries, pass
        /// isCoated=true and the original (un-inflated) bond/top polygons.
        /// For bare geometries, pass isCoated=false; bondPolygon and topPolygon
        /// can be null.
        /// </summary>
        public static Mesh3D MeshAndRevolve(
            TopoDS_Compound  fragmentedCompound,
            bool             isCoated,
            List<gp_Pnt>     bondPolygon,
            List<gp_Pnt>     topPolygon,
            Options          options = null)
        {
            if (options == null) options = new Options();
            Mesh2D mesh2d = Generate2D(fragmentedCompound, isCoated,
                                       bondPolygon, topPolygon, options);
            if (!string.IsNullOrEmpty(options.Dump2DMeshTo))
                WriteToInp(mesh2d, options.Dump2DMeshTo);
            return Revolve(mesh2d, options);
        }

        /// <summary>
        /// Generate only the 2D axisymmetric mesh and return it in-memory.
        /// </summary>
        public static Mesh2D Generate2D(
            TopoDS_Compound  fragmentedCompound,
            bool             isCoated,
            List<gp_Pnt>     bondPolygon,
            List<gp_Pnt>     topPolygon,
            Options          options = null)
        {
            if (options == null) options = new Options();

            string tmpStep = Path.Combine(Path.GetTempPath(),
                $"mesher_{Guid.NewGuid():N}.step");
            string tmpInp  = Path.Combine(Path.GetTempPath(),
                $"mesher_{Guid.NewGuid():N}.inp");

            try
            {
                ExportCompoundAsStep(fragmentedCompound, tmpStep);
                InvokeGmsh(tmpStep, isCoated, bondPolygon, topPolygon,
                           options, tmpInp);
                Mesh2D mesh2d = ParseInpAsMesh2D(tmpInp, isCoated, options);
                return mesh2d;
            }
            finally
            {
                try { if (File.Exists(tmpStep)) File.Delete(tmpStep); } catch { }
                try { if (File.Exists(tmpInp )) File.Delete(tmpInp ); } catch { }
            }
        }

        /// <summary>
        /// Revolve a 2D mesh into a 3D wedge sector.
        /// </summary>
        public static Mesh3D Revolve(Mesh2D mesh2d, Options options = null)
        {
            if (options == null) options = new Options();
            return RevolveImpl(mesh2d, options);
        }

        public static void WriteToInp(Mesh2D mesh, string path)
        {
            Write2D(mesh, path);
        }

        public static void WriteToInp(Mesh3D mesh, string path)
        {
            Write3D(mesh, path);
        }

        // ====================================================================
        //  2D PIPELINE -- STEP EXPORT
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
        //  2D PIPELINE -- GMSH RUN
        // ====================================================================

        private static void InvokeGmsh(
            string       stepPath,
            bool         isCoated,
            List<gp_Pnt> bondPoly,
            List<gp_Pnt> topPoly,
            Options      opts,
            string       outInpPath)
        {
            // Gmsh's writer infers format from file extension; .inp is Abaqus.
            string gmshOut = outInpPath + ".gmsh.tmp.inp";

            Gmsh.Initialize();
            try
            {
                // ---- options ------------------------------------------------
                Gmsh.Option.SetNumber("General.Terminal",
                                      opts.QuietGmsh ? 0 : 1);
                Gmsh.Option.SetNumber("Mesh.Algorithm",       opts.MeshAlgorithm);
                Gmsh.Option.SetNumber("Mesh.ElementOrder",    2);
                Gmsh.Option.SetNumber("Mesh.SecondOrderIncomplete", 0);
                Gmsh.Option.SetNumber("Mesh.MeshSizeMin",
                                      isCoated ? opts.CoatingMeshSize : opts.MeshSize);
                Gmsh.Option.SetNumber("Mesh.MeshSizeMax",     opts.MeshSize);
                Gmsh.Option.SetNumber("Mesh.SaveAll",         0);
                Gmsh.Option.SetNumber("Mesh.SaveGroupsOfNodes", 1);
                Gmsh.Option.SetNumber("Mesh.Smoothing",       opts.Smoothing);
                Gmsh.Option.SetNumber("Mesh.Optimize",        opts.OptimizeMesh ? 1 : 0);
                Gmsh.Option.SetNumber("Mesh.OptimizeNetgen",  opts.OptimizeMesh ? 1 : 0);

                // ---- import + topology unification -------------------------
                _ = Gmsh.Model.Occ.ImportShapes(stepPath, false, "");
                Gmsh.Model.Occ.RemoveAllDuplicates();
                Gmsh.Model.Occ.Synchronize();

                // ---- enumerate all 2D faces --------------------------------
                (int, int)[] faceDimTags = Gmsh.Model.GetEntities(2);

                // ---- classify by centroid (innermost-wins) -----------------
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

                // ---- tag physical groups -----------------------------------
                AddPhysicalSurface(sealTags, opts.SealGroupName);
                if (isCoated)
                {
                    AddPhysicalSurface(bondTags, opts.BondGroupName);
                    AddPhysicalSurface(topTags,  opts.TopGroupName);
                }

                // ---- per-region size for thin coatings ---------------------
                if (isCoated && (bondTags.Count + topTags.Count) > 0)
                {
                    var coatDimTags = bondTags.Concat(topTags)
                                              .Select(t => (2, t))
                                              .ToArray();
                    Gmsh.Model.Mesh.SetSize(coatDimTags, opts.CoatingMeshSize);
                }

                // ---- adaptive transfinite tip discretization ---------------
                if (opts.UseTransfiniteTip)
                    ApplyTransfiniteTip(sealTags, opts);

                // ---- mesh and write to temp .inp ---------------------------
                Gmsh.Model.Mesh.Generate(2);
                Gmsh.Write(gmshOut);

                // ---- post-process gmsh's .inp:
                //      CPS6 -> CAX6, and gmsh "Surface<N>" elset names
                //      get renamed by matching node-set equality with the
                //      corresponding NSETs (which already carry the physical
                //      group names like SEAL / BOND_COAT / TOP_COAT).
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
        //  2D PIPELINE -- ADAPTIVE TRANSFINITE TIP
        // ====================================================================

        // Identify the substrate's outer tip edges -- the edges with the
        // largest X centroid -- and apply SetTransfiniteCurve so the angular
        // node count is deterministic and geometry-aware. Mirrors the APDL
        // pattern of LESIZE on specific lines with lineDivideNumber.
        private static void ApplyTransfiniteTip(List<int> sealFaceTags,
                                                Options    opts)
        {
            if (sealFaceTags == null || sealFaceTags.Count == 0) return;

            // Collect edges that border substrate faces, with their max-X
            // sampled along the curve.
            var edgeMaxX = new Dictionary<int, double>();
            var edgeLen  = new Dictionary<int, double>();

            foreach (int faceTag in sealFaceTags)
            {
                // GetBoundary(dimTags, combined=false, oriented=false, recursive=false)
                // Wrapper signatures vary; assume positional return-style.
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

                    // Approximate edge length via mass-property (length of a
                    // 1D entity is its "mass" with unit density).
                    double len = ApproxEdgeLength(eTag);
                    edgeLen[eTag] = len;
                }
            }

            if (edgeMaxX.Count == 0) return;

            double globalMaxX = edgeMaxX.Values.Max();
            // Pick edges within a thin band of the max X -- those are the tip
            // edges. Band width = larger of TipNodeSpacing or 1% of max X.
            double band = Math.Max(opts.TipNodeSpacing,
                                   Math.Abs(globalMaxX) * 0.01);

            foreach (var kv in edgeMaxX)
            {
                int eTag    = kv.Key;
                double midX = kv.Value;
                if (midX < globalMaxX - band) continue;

                double L = edgeLen.TryGetValue(eTag, out var l) ? l : 0.0;
                if (L <= 0.0) continue;

                int nodes = (int)Math.Round(L / opts.TipNodeSpacing) + 1;
                if (nodes < opts.MinTipNodes) nodes = opts.MinTipNodes;
                if (nodes > opts.MaxTipNodes) nodes = opts.MaxTipNodes;

                // Gmsh.Model.Mesh.SetTransfiniteCurve(int tag, int nNodes,
                //                                     string meshType, double coef)
                // meshType = "Progression", coef = 1.0 for uniform spacing.
                Gmsh.Model.Mesh.SetTransfiniteCurve(eTag, nodes,
                                                    "Progression", 1.0);
            }
        }

        private static double ApproxEdgeLength(int edgeTag)
        {
            // Use the "mass" of a 1D entity -- which is its length when
            // density is unity -- via Gmsh.Model.Occ.GetMass.
            try
            {
                double mass;
                Gmsh.Model.Occ.GetMass(1, edgeTag, out mass);
                return Math.Abs(mass);
            }
            catch
            {
                return 0.0;
            }
        }

        // ====================================================================
        //  2D PIPELINE -- INP REWRITE (CPS6 -> CAX6 + elset rename)
        // ====================================================================

        private static void RewriteGmshInp(string srcPath, string dstPath)
        {
            // First pass: load the entire file into intermediate structures.
            var nodes   = new Dictionary<int, double[]>();
            var elemsByElset = new Dictionary<string,
                                   List<int[]>>();   // [eid, n1..n6]
            var nsetByName   = new Dictionary<string, HashSet<int>>();
            // Preserve insertion order so the output is stable.
            var elsetOrder  = new List<string>();
            var nsetOrder   = new List<string>();

            ParseInpStreaming(srcPath, nodes, elemsByElset, elsetOrder,
                              nsetByName, nsetOrder);

            // Build elset -> renamed-name map by matching elset's node-set to
            // an nset with the same nodes. The physical-group names live on
            // the NSETs (gmsh's writer pattern); the ELSETs come out as
            // "Surface<N>" which we want to replace.
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

            // Write the rewritten inp with CAX6 and renamed elsets.
            using (var sw = new StreamWriter(dstPath))
            {
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("** Mesher 2D output (CAX6, per-region elsets)");
                sw.WriteLine("** ----------------------------------------------");

                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in nodes.OrderBy(k => k.Key))
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, 0.0",
                        kv.Key, kv.Value[0], kv.Value[1]));
                }

                foreach (string elsetName in elsetOrder)
                {
                    string outName = elsetRename[elsetName];
                    sw.WriteLine("*ELEMENT, TYPE=CAX6, ELSET=" + outName);
                    foreach (int[] e in elemsByElset[elsetName])
                    {
                        // e[0] = eid, e[1..6] = connectivity
                        sw.Write(e[0]);
                        for (int j = 1; j < e.Length; j++)
                        {
                            sw.Write(",");
                            sw.Write(e[j]);
                        }
                        sw.WriteLine();
                    }
                }

                foreach (string nsetName in nsetOrder)
                {
                    var nset = nsetByName[nsetName];
                    sw.WriteLine("*NSET, NSET=" + nsetName);
                    WriteIdList(sw, nset.OrderBy(x => x));
                }
            }
        }

        private static HashSet<int> NodesOfElset(List<int[]> elems)
        {
            var s = new HashSet<int>();
            foreach (int[] e in elems)
                for (int j = 1; j < e.Length; j++)
                    s.Add(e[j]);
            return s;
        }

        // ====================================================================
        //  INP PARSER -- shared by Generate2D and the rewrite step
        // ====================================================================

        private static void ParseInpStreaming(
            string                                    path,
            Dictionary<int, double[]>                 nodes,
            Dictionary<string, List<int[]>>           elemsByElset,
            List<string>                              elsetOrder,
            Dictionary<string, HashSet<int>>          nsetByName,
            List<string>                              nsetOrder)
        {
            string section = null;
            string sectName = null;
            int    elemNodeCount = 0; // 6 for CAX6/CPS6/T6

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
                        if (IsQuadTri6(typeTok)) elemNodeCount = 6;
                        else                     elemNodeCount = 0;
                        sectName = FindAssignment(line, "elset");
                        if (sectName != null && elemNodeCount == 6)
                        {
                            if (!elemsByElset.ContainsKey(sectName))
                            {
                                elemsByElset[sectName] = new List<int[]>();
                                elsetOrder.Add(sectName);
                            }
                        }
                    }
                    else if (head == "*nset")
                    {
                        section = "nset";
                        sectName = FindAssignment(line, "nset");
                        if (sectName != null && !nsetByName.ContainsKey(sectName))
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
                else if (section == "elem" && sectName != null && elemNodeCount == 6)
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

        private static Mesh2D ParseInpAsMesh2D(string path, bool isCoated,
                                               Options opts)
        {
            var nodes  = new Dictionary<int, double[]>();
            var elsets = new Dictionary<string, List<int[]>>();
            var nsets  = new Dictionary<string, HashSet<int>>();
            var elsetOrder = new List<string>();
            var nsetOrder  = new List<string>();
            ParseInpStreaming(path, nodes, elsets, elsetOrder, nsets, nsetOrder);

            return new Mesh2D {
                Nodes = nodes,
                ElementsByRegion = elsets,
                NSets = nsets,
            };
        }

        // ====================================================================
        //  3D REVOLVE -- the C# port of revolve_mesh.py
        // ====================================================================

        // Layer scheme (matches Python v2):
        //   corner layer k, k = 0..N      : every 2D node gets a copy
        //                                    at theta = k * S / N
        //   vmid   layer k, k = 0..N-1    : only 2D-corner nodes get a copy
        //                                    at theta = (k + 0.5) * S / N
        // C3D15 orientation (matches Python v2):
        //   Face S1 (1-3) at corner layer k+1 (higher theta, more -Z)
        //   Face S2 (4-6) at corner layer k   (lower  theta)
        // LEFT_SURF  = all node copies at theta = 0
        // RIGHT_SURF = all node copies at theta = S
        // FIX_FACES  = all interior + master copies of fix-face 2D nodes
        //              (excluding layer-0 copies, which are MPC-slave).
        private static Mesh3D RevolveImpl(Mesh2D mesh2d, Options opts)
        {
            int N = opts.WedgesPerSector;
            if (N < 1)
                throw new ArgumentException(
                    "WedgesPerSector must be >= 1");

            double sectorRad = opts.SectorAngleDeg * Math.PI / 180.0;

            var nodes2d = mesh2d.Nodes;
            if (nodes2d.Count == 0)
                throw new InvalidOperationException(
                    "2D mesh has no nodes -- nothing to revolve");

            // Collect all 2D elements with their region tags. Flatten so we
            // can iterate uniformly while preserving the region for each.
            var flatElems = new List<(int eid, int[] conn, string region)>();
            foreach (var kv in mesh2d.ElementsByRegion)
                foreach (int[] e in kv.Value)
                    flatElems.Add((e[0], Slice(e, 1, 6), kv.Key));

            if (flatElems.Count == 0)
                throw new InvalidOperationException(
                    "2D mesh has no elements -- nothing to revolve");

            // Classify 2D nodes as corner-type vs midside-type by inspecting
            // their role in each CAX6 element (1-3 = corner, 4-6 = midside).
            var cornerNodes = new HashSet<int>();
            var midsideNodes = new HashSet<int>();
            foreach (var ee in flatElems)
            {
                for (int j = 0; j < 3; j++) cornerNodes.Add(ee.conn[j]);
                for (int j = 3; j < 6; j++) midsideNodes.Add(ee.conn[j]);
            }
            midsideNodes.ExceptWith(cornerNodes); // overlap -> treat as corner

            // FIX_FACES 2D source: try standard names from APDL/legacy
            // convention. Falls back to empty if no matching nset exists.
            var fix2d = LookupNset(mesh2d.NSets,
                "RIGHTANDLEFT", "FIX_FACES", "FIX_TOP", "FIX_BOT");

            // ID-offset scheme: round-up the max 2D ID to the next power of
            // ten so each (layer, kind) gets a clean ID range.
            int max2dId = nodes2d.Keys.Max();
            long baseOff = (long)Math.Pow(10.0,
                              Math.Floor(Math.Log10(Math.Max(1, max2dId))) + 1);
            // corner layer k offset = k * base
            // vmid   layer k offset = (N + 1 + k) * base

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

            var nodes3d = new Dictionary<int, double[]>(
                nodes2d.Count * (2 * N + 1));
            var leftSet  = new HashSet<int>();
            var rightSet = new HashSet<int>();
            var fixSet   = new HashSet<int>();

            // ---- corner layers -----------------------------------------
            for (int k = 0; k <= N; k++)
            {
                double th = ThetaCorner(k);
                long off  = CornerOff(k);
                foreach (var kv in nodes2d)
                {
                    int    nid = kv.Key;
                    double x   = kv.Value[0];
                    double y   = kv.Value[1];
                    double[] XYZ = RotateY(x, y, th);
                    int newId = checked((int)(nid + off));
                    nodes3d[newId] = XYZ;
                    if (k == 0) leftSet .Add(newId);
                    if (k == N) rightSet.Add(newId);
                    // FIX_FACES: include all corner-layer copies except
                    // layer-0 (which is the MPC-slave side).
                    if (k != 0 && fix2d.Contains(nid)) fixSet.Add(newId);
                }
            }

            // ---- vmid layers -------------------------------------------
            for (int k = 0; k < N; k++)
            {
                double th = ThetaVmid(k);
                long off  = VmidOff(k);
                foreach (int nid in cornerNodes)
                {
                    if (!nodes2d.TryGetValue(nid, out var xy)) continue;
                    double[] XYZ = RotateY(xy[0], xy[1], th);
                    int newId = checked((int)(nid + off));
                    nodes3d[newId] = XYZ;
                    if (fix2d.Contains(nid)) fixSet.Add(newId);
                }
            }

            // ---- C3D15 connectivity, per region ------------------------
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

            // Translate region names: SEAL -> SEAL_ELEMS, etc.
            var elemsOut = new Dictionary<string, List<int[]>>();
            foreach (var kv in elems3dByRegion)
            {
                string outName = Map2DRegionTo3DName(kv.Key, opts);
                elemsOut[outName] = kv.Value;
            }

            var nsetsOut = new Dictionary<string, HashSet<int>>
            {
                [opts.LeftSurfNset ] = leftSet,
                [opts.RightSurfNset] = rightSet,
                [opts.FixFacesNset ] = fixSet,
            };

            return new Mesh3D
            {
                Nodes = nodes3d,
                ElementsByRegion = elemsOut,
                NSets = nsetsOut,
                SectorAngleRad = sectorRad,
                WedgesPerSector = N,
            };
        }

        private static string Map2DRegionTo3DName(string in2D, Options opts)
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
            {
                foreach (var kv in nsets)
                    if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
            }
            return new HashSet<int>();
        }

        private static int[] Slice(int[] a, int from, int count)
        {
            int[] r = new int[count];
            Array.Copy(a, from, r, 0, count);
            return r;
        }

        // ====================================================================
        //  OUTPUT WRITERS
        // ====================================================================

        private static void Write2D(Mesh2D mesh, string path)
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("** Mesher 2D mesh dump");
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in mesh.Nodes.OrderBy(k => k.Key))
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, 0.0",
                        kv.Key, kv.Value[0], kv.Value[1]));
                }
                foreach (var kv in mesh.ElementsByRegion)
                {
                    sw.WriteLine("*ELEMENT, TYPE=CAX6, ELSET=" + kv.Key);
                    foreach (int[] e in kv.Value) WriteElement(sw, e);
                }
                foreach (var kv in mesh.NSets)
                {
                    sw.WriteLine("*NSET, NSET=" + kv.Key);
                    WriteIdList(sw, kv.Value.OrderBy(x => x));
                }
            }
        }

        private static void Write3D(Mesh3D mesh, string path)
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("** Mesher 3D wedge mesh");
                sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "** Sector angle (rad): {0:R}",  mesh.SectorAngleRad));
                sw.WriteLine("** Wedges/sector  : " + mesh.WedgesPerSector);
                sw.WriteLine("** ----------------------------------------------");
                sw.WriteLine("*NODE, NSET=NALL");
                foreach (var kv in mesh.Nodes.OrderBy(k => k.Key))
                {
                    double[] xyz = kv.Value;
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}, {1:R}, {2:R}, {3:R}",
                        kv.Key, xyz[0], xyz[1], xyz[2]));
                }
                foreach (var kv in mesh.ElementsByRegion)
                {
                    sw.WriteLine("*ELEMENT, TYPE=C3D15, ELSET=" + kv.Key);
                    foreach (int[] e in kv.Value) WriteElement(sw, e);
                }
                foreach (var kv in mesh.NSets)
                {
                    sw.WriteLine("*NSET, NSET=" + kv.Key);
                    WriteIdList(sw, kv.Value.OrderBy(x => x));
                }
            }
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
            int col = 0;
            var buf = new List<int>();
            foreach (int id in ids)
            {
                buf.Add(id);
                col++;
                if (col == 8)
                {
                    sw.WriteLine(string.Join(", ", buf));
                    buf.Clear();
                    col = 0;
                }
            }
            if (buf.Count > 0) sw.WriteLine(string.Join(", ", buf));
        }
    }
}
