// =============================================================================
//  LoadingSpec
//
//  Configuration bag for the cyclic-modal analysis loading and frequency
//  extraction. The geometry, mesh, and materials live elsewhere; this
//  carries the operating-point quantities (rotation speed) plus the
//  eigen-solver scope (mode count, nodal-diameter range).
//
//  Defaults mirror the values used in the existing disc_modal.inp and
//  apdlmacro.txt reference runs:
//      OmegaY=10000 rpm, Nmodes=10, nodal diameters 0..12.
// =============================================================================

namespace LakeCore
{
    public class LoadingSpec
    {
        // -------- Rotation -----------------------------------------------
        // Rotor speed in revolutions per minute, applied as a centrifugal
        // load (CalculiX *DLOAD CENTRIF). The writer converts to omega^2
        // in (rad/s)^2 internally.
        public double AngularVelocityRpm = 10000.0;

        // -------- Eigen-solver scope -------------------------------------
        public int    NumModes           = 10;
        public int    NodalDiameterMin   = 0;
        public int    NodalDiameterMax   = 12;

        // -------- Cosmetics ----------------------------------------------
        public string HeadingText
            = "Labyrinth seal -- prestressed cyclic-symmetric modal";
    }
}
