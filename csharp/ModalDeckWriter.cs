// =============================================================================
//  ModalDeckWriter
//
//  Writes a CalculiX cyclic-symmetric modal-analysis deck from a Mesher3D
//  output plus material and loading specifications. The deck is a faithful
//  port of runs/cyclic_modal_v3_ang5/disc_modal.inp generalised to N
//  material regions and parameterised loading.
//
//  Pipeline (run after Mesher pipeline):
//      var mesh = Mesher.MeshAndRevolve(...);
//      ModalDeckWriter.Write(mesh, substrateMaterial, coating, loading, path);
//      // -> path is a self-contained .inp with mesh inlined
//
//  Material convention (matches coatedapdlmacro.txt):
//      Substrate uses MaterialModalAnalysis (youngModulus, poisson, density).
//      Bond coat and top coat reuse the substrate's E and nu, swap in
//      density from CoatingInfo. No coat-specific E/nu is exposed because
//      the existing domain model doesn't carry it.
//
//  Two-step analysis written into the deck:
//      Step 1: NLGEOM static with centrifugal preload (omega^2 from rpm)
//      Step 2: *FREQUENCY perturbation with *SELECT CYCLIC SYMMETRY MODES
//
//  Template is a single embedded const string with {{PLACEHOLDER}} tokens
//  resolved at write time.
// =============================================================================

using System;
using System.Globalization;
using System.IO;

namespace LakeCore
{
    public static class ModalDeckWriter
    {
        // Material-name conventions used inside the deck.
        private const string SUBSTRATE_MAT = "SUBSTRATE_MAT";
        private const string BOND_COAT_MAT = "BOND_COAT_MAT";
        private const string TOP_COAT_MAT  = "TOP_COAT_MAT";

        // ELSET-name conventions (must match MesherOptions defaults).
        private const string SEAL_ELSET = "SEAL_ELEMS";
        private const string BOND_ELSET = "BOND_COAT_ELEMS";
        private const string TOP_ELSET  = "TOP_COAT_ELEMS";

        // ====================================================================
        //  PUBLIC API
        // ====================================================================

        /// <summary>
        /// Write a self-contained CalculiX deck. The mesh is inlined directly
        /// (no *INCLUDE), so the resulting .inp is the only file CalculiX
        /// needs at the command line.
        /// </summary>
        public static void Write(
            Mesher3D              mesh,
            MaterialModalAnalysis substrateMaterial,
            CoatingInfo           coating,
            LoadingSpec           loading,
            string                outputDeckPath)
        {
            if (mesh              == null) throw new ArgumentNullException(nameof(mesh));
            if (substrateMaterial == null) throw new ArgumentNullException(nameof(substrateMaterial));
            if (loading           == null) throw new ArgumentNullException(nameof(loading));

            // Number of base sectors (e.g. 24 for 15-degree datum sector).
            // mesh.SectorAngleRad is what the revolver was told to build.
            int nSectors = (int)Math.Round(2.0 * Math.PI / mesh.SectorAngleRad);

            // Centrifugal load wants omega^2 in (rad/s)^2.
            double omegaRad     = loading.AngularVelocityRpm * 2.0 * Math.PI / 60.0;
            double omegaSquared = omegaRad * omegaRad;

            // Resolve placeholders.
            string deck = TEMPLATE
                .Replace("{{HEADING_TEXT}}",
                         loading.HeadingText ?? "")
                .Replace("{{MESH_BLOCK}}",
                         BuildMeshBlock(mesh))
                .Replace("{{MATERIAL_BLOCKS}}",
                         BuildMaterialBlocks(substrateMaterial, coating))
                .Replace("{{SECTION_BLOCKS}}",
                         BuildSectionBlocks(mesh))
                .Replace("{{N_SECTORS}}",
                         nSectors.ToString(CultureInfo.InvariantCulture))
                .Replace("{{DLOAD_BLOCKS}}",
                         BuildDloadBlocks(mesh, omegaSquared))
                .Replace("{{N_MODES}}",
                         loading.NumModes.ToString(CultureInfo.InvariantCulture))
                .Replace("{{ND_MIN}}",
                         loading.NodalDiameterMin.ToString(CultureInfo.InvariantCulture))
                .Replace("{{ND_MAX}}",
                         loading.NodalDiameterMax.ToString(CultureInfo.InvariantCulture));

            File.WriteAllText(outputDeckPath, deck);
        }

        // ====================================================================
        //  BLOCK BUILDERS
        // ====================================================================

        private static string BuildMeshBlock(Mesher3D mesh)
        {
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                mesh.WriteToStream(sw);
                return sw.ToString().TrimEnd();
            }
        }

        // Substrate always present. Coat materials only if coating != null
        // and HasCoating. All three share substrate's E and nu by design
        // of the CoatingInfo class (no coat-specific elastic constants).
        private static string BuildMaterialBlocks(
            MaterialModalAnalysis substrate, CoatingInfo coating)
        {
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                EmitMaterial(sw, SUBSTRATE_MAT,
                             substrate.youngModulus,
                             substrate.poisson,
                             substrate.density);

                if (coating != null && coating.HasCoating)
                {
                    sw.WriteLine("**");
                    EmitMaterial(sw, BOND_COAT_MAT,
                                 substrate.youngModulus,
                                 substrate.poisson,
                                 coating.BondCoatDensity);
                    sw.WriteLine("**");
                    EmitMaterial(sw, TOP_COAT_MAT,
                                 substrate.youngModulus,
                                 substrate.poisson,
                                 coating.TopCoatDensity);
                }

                return sw.ToString().TrimEnd();
            }
        }

        private static void EmitMaterial(TextWriter sw, string name,
                                         double e, double nu, double rho)
        {
            sw.WriteLine("*MATERIAL, NAME=" + name);
            sw.WriteLine("*ELASTIC");
            sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:R}, {1:R}", e, nu));
            sw.WriteLine("*DENSITY");
            sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:R}", rho));
        }

        // One *SOLID SECTION per ELSET actually present in the mesh, so the
        // bare case emits only the substrate section and the coated case
        // emits all three.
        private static string BuildSectionBlocks(Mesher3D mesh)
        {
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                foreach (var kv in mesh.ElementsByRegion)
                {
                    string elset    = kv.Key;
                    string material = RegionToMaterialName(elset);
                    sw.WriteLine("*SOLID SECTION, ELSET=" + elset
                                 + ", MATERIAL=" + material);
                }
                return sw.ToString().TrimEnd();
            }
        }

        // CENTRIF needs (omega^2, cx, cy, cz, ax, ay, az). Axis of rotation
        // is global +Y so axis = (0,0,0) -> (0,1,0), matching the cyclic
        // symmetry axis declared above in the template.
        private static string BuildDloadBlocks(Mesher3D mesh, double omegaSquared)
        {
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                string omegaStr = omegaSquared.ToString("R",
                                                         CultureInfo.InvariantCulture);
                foreach (var kv in mesh.ElementsByRegion)
                {
                    sw.WriteLine(kv.Key + ", CENTRIF, " + omegaStr
                                 + ", 0., 0., 0., 0., 1., 0.");
                }
                return sw.ToString().TrimEnd();
            }
        }

        private static string RegionToMaterialName(string region)
        {
            if (region == BOND_ELSET) return BOND_COAT_MAT;
            if (region == TOP_ELSET ) return TOP_COAT_MAT;
            return SUBSTRATE_MAT;  // SEAL_ELSET and anything else default here
        }

        // ====================================================================
        //  TEMPLATE
        // ====================================================================

        private const string TEMPLATE =
@"**=====================================================================
** {{HEADING_TEXT}}
**
** Generated by LakeCore.ModalDeckWriter
** Units: MM_TON_S_C (length=mm, mass=tonne, time=s, temp=Celsius)
**   -> stress in MPa, density in t/mm^3, omega^2 in (rad/s)^2
**=====================================================================
*HEADING
{{HEADING_TEXT}}
**
**---------------------------------------------------------------------
** 1) MESH  (nodes, C3D15 elements, LEFT_SURF / RIGHT_SURF / FIX_FACES)
**---------------------------------------------------------------------
{{MESH_BLOCK}}
**
**---------------------------------------------------------------------
** 2) MATERIALS  (E and nu shared across all regions; density per region)
**---------------------------------------------------------------------
{{MATERIAL_BLOCKS}}
**
**---------------------------------------------------------------------
** 3) SECTIONS  -- one per ELSET present in the mesh
**---------------------------------------------------------------------
{{SECTION_BLOCKS}}
**
**---------------------------------------------------------------------
** 4) CYCLIC SYMMETRY  (LEFT_SURF slave, RIGHT_SURF master, axis +Y)
**
**    *SURFACE wraps the nodal-nset as a node-surface usable by *TIE.
**    *TIE, CYCLIC SYMMETRY tells CCX the two surfaces bound the datum
**    sector; CCX auto-generates the complex MPCs between matching node
**    pairs (which match exactly by construction in the revolver).
**---------------------------------------------------------------------
*SURFACE, NAME=LEFT_SURF_S, TYPE=NODE
LEFT_SURF
*SURFACE, NAME=RIGHT_SURF_S, TYPE=NODE
RIGHT_SURF
**
*TIE, NAME=CYCSYM, CYCLIC SYMMETRY
LEFT_SURF_S, RIGHT_SURF_S
**
*CYCLIC SYMMETRY MODEL, N={{N_SECTORS}}, NGRAPH=1, TIE=CYCSYM
0., 0., 0., 0., 1., 0.
**
**---------------------------------------------------------------------
** 5) BOUNDARY  (mounting faces clamped)
**
**    FIX_FACES excludes LEFT_SURF copies of the fix-face nodes because
**    those are slaves of the cyclic-symmetry tie and CCX forbids slave
**    nodes from also carrying SPCs. The cyclic MPC propagates SPC=0 to
**    layer 0 implicitly: U_left = U_right * exp(i*phase) = 0.
**---------------------------------------------------------------------
*BOUNDARY
FIX_FACES, 1, 3, 0.
**
**=====================================================================
** STEP 1 -- static centrifugal preload, nonlinear geometry
**
**   omega = rpm * 2*pi/60  -> omega^2 used by CENTRIF
**=====================================================================
*STEP, NLGEOM=YES, INC=50
*STATIC
0.1, 1.0
**
*DLOAD
{{DLOAD_BLOCKS}}
**
*NODE FILE
U
*EL FILE
S, E
*END STEP
**
**=====================================================================
** STEP 2 -- perturbation modal with cyclic symmetry sweep
**
**   *FREQUENCY, STORAGE=YES writes jobname.eig (CCX equivalent of the
**   ANSYS .rstp file) so a subsequent *MODAL DYNAMIC / *STEADY STATE
**   DYNAMICS run can reuse the eigensolution.
**=====================================================================
*STEP, PERTURBATION
*FREQUENCY, STORAGE=YES
{{N_MODES}}
*SELECT CYCLIC SYMMETRY MODES, NMIN={{ND_MIN}}, NMAX={{ND_MAX}}
**
*NODE FILE
U, PU
*EL FILE
S
*END STEP
";
    }
}
