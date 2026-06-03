// =============================================================================
//  Mesher
//
//  Thin orchestrator over Mesher2D and Mesher3D. Provides a single-call API
//  for the common case "compound in, 3D wedge mesh out", and honours the
//  Dump2DMeshTo option so the intermediate 2D mesh can be inspected without
//  staging the calls by hand.
//
//  One-call use:
//      var mesh3d = Mesher.MeshAndRevolve(
//          compound, isCoated: true,
//          bondPolygon: bondPoints,
//          topPolygon:  topPoints,
//          options: new MesherOptions { ... });
//      mesh3d.WriteToInp(@"...\mesh3d.inp");
//
//  For staged calls (mesh, inspect, then revolve), use the underlying classes
//  directly:
//      var m2d = Mesher2D.Generate(compound, isCoated, bond, top, opts);
//      // ... inspect m2d ...
//      var m3d = Mesher3D.Revolve(m2d, opts);
// =============================================================================

using System.Collections.Generic;

namespace LakeCore
{
    public static class Mesher
    {
        /// <summary>
        /// Mesh the OCC compound in 2D and revolve into a 3D wedge sector
        /// in a single call. If options.Dump2DMeshTo is non-null, the
        /// intermediate 2D mesh is also written to that path.
        /// </summary>
        public static Mesher3D MeshAndRevolve(
            TopoDS_Compound  fragmentedCompound,
            bool             isCoated,
            List<gp_Pnt>     bondPolygon,
            List<gp_Pnt>     topPolygon,
            MesherOptions    options = null)
        {
            if (options == null) options = new MesherOptions();

            Mesher2D mesh2d = Mesher2D.Generate(
                fragmentedCompound, isCoated, bondPolygon, topPolygon, options);

            if (!string.IsNullOrEmpty(options.Dump2DMeshTo))
                mesh2d.WriteToInp(options.Dump2DMeshTo);

            return Mesher3D.Revolve(mesh2d, options);
        }
    }
}
