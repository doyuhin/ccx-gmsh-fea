/*
 * HoneyCombParameters — geometry of the honeycomb coating on the counterpart
 * land. Used by the tooth acoustic check to correct the acoustic wavelength
 * (HoneycombCalculator root-find).
 *
 * Single home: owned by GeometricParameters (the duplicate that used to hang
 * directly off SealCriteria has been removed).
 *
 * Units: all lengths in mm, consistent with GeometricParameters.
 */
using System;

namespace LakeCore
{
    public class HoneyCombParameters : LakeComponent
    {
        public override string Name { get; set; } = "Honeycomb Parameters";
        public override LakeComponentType ToothType { get; set; } = LakeComponentType.HoneycombParams;

        public double s { get; set; } = 0.0;   // radial depth of honeycomb cavity
        public double a { get; set; } = 0.0;   // cell size
        public double L { get; set; } = 0.0;   // cell pitch
    }
}
