// =============================================================================
//  MesherOptions
//
//  Configuration bag for the 2D meshing + 3D revolve pipeline. Shared across
//  Mesher / Mesher2D / Mesher3D so each stage reads its own knobs from the
//  same instance.
//
//  Defaults are tuned to match the ANSYS APDL macros that historically
//  produced the seal meshes (apdlmacro.txt and coatedapdlmacro.txt) so a
//  first run reproduces the existing reference numbers without manual tuning.
// =============================================================================

namespace LakeCore
{
    public sealed class MesherOptions
    {
        // ------------------ 2D mesh sizing --------------------------------
        public double MeshSize            = 0.5;   // mm, substrate / bulk
        public double CoatingMeshSize     = 0.15;  // mm, bond + top coats
        public int    MeshAlgorithm       = 6;     // 6 = Frontal-Delaunay
        public bool   QuietGmsh           = true;

        // ------------------ 2D mesh quality (APDL SMRTSIZE,,,4,3) --------
        public int    Smoothing           = 3;
        public bool   OptimizeMesh        = true;

        // ------------------ Adaptive tip discretisation ------------------
        // Mirrors APDL's lineDivideNumber = 18/meshSize -- enforces a fixed,
        // geometry-aware element count on the substrate's tip edges via
        // gmsh's transfinite-curve mechanism. Set UseTransfiniteTip = false
        // to let the global MeshSize / CoatingMeshSize drive the tip count.
        public bool   UseTransfiniteTip   = true;
        public double TipNodeSpacing      = 0.05;  // mm, target spacing
        public int    MinTipNodes         = 5;
        public int    MaxTipNodes         = 30;

        // ------------------ 3D revolve -----------------------------------
        public double SectorAngleDeg      = 15.0;
        public int    WedgesPerSector     = 5;

        // ------------------ 2D physical-group names ----------------------
        // Used by Mesher2D when tagging gmsh physical groups, and by the
        // Surface<N> -> physical-name ELSET rename pass that runs after
        // gmsh's INP writer.
        public string SealGroupName       = "SEAL";
        public string BondGroupName       = "BOND_COAT";
        public string TopGroupName        = "TOP_COAT";

        // ------------------ 3D output naming -----------------------------
        // Mesher3D maps 2D region names to these 3D ELSET names. The cyclic
        // MPC nsets and the boundary-condition nset are also configurable.
        public string SealElset3D         = "SEAL_ELEMS";
        public string BondElset3D         = "BOND_COAT_ELEMS";
        public string TopElset3D          = "TOP_COAT_ELEMS";
        public string LeftSurfNset        = "LEFT_SURF";
        public string RightSurfNset       = "RIGHT_SURF";
        public string FixFacesNset        = "FIX_FACES";

        // ------------------ BC tagging (axial-extreme mount edges) -------
        // Mesher2D auto-discovers the seal's two mounting edges via the
        // model bounding box and tags them as separate physical curves
        // with these names. The 2D INP carries them as NSETs; Mesher3D
        // combines them into the FIX_FACES nset for cyclic-modal use.
        // BoundingBoxTolerance is the slack used when matching an edge's
        // own bbox to the global yMin / yMax line (mm).
        public string FixTopCurveName     = "FIX_TOP";
        public string FixBotCurveName     = "FIX_BOT";
        public double BoundingBoxTolerance = 1e-5;

        // ------------------ Debug ---------------------------------------
        // If non-null, the 2D mesh is also written to this path before the
        // 3D revolve runs. Useful for inspecting the intermediate.
        public string Dump2DMeshTo        = null;
    }
}
