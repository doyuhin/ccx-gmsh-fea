/*
 * GeometricParameters — dimensional inputs for the criteria checks.
 *
 * IMPORTANT unit convention: diameters are stored AS DIAMETERS (matching the
 * drawings). Formulas derived for a radius (structural lambda = 3R²/(n²l²))
 * receive D/2 at the call site inside the calculators — see SealMath.
 * All lengths in mm, areas in mm².
 *
 * Property names are kept identical to the original program so reflection-
 * based UI bindings (LakeComponent.SetValueToProperty) keep working.
 */
using System;

namespace LakeCore
{
    public class GeometricParameters : LakeComponent
    {
        public override string Name { get; set; } = "Geometric Parameters";
        public override LakeComponentType ToothType { get; set; } = LakeComponentType.Geometry;

        public double toothOuterDiameter { get; set; } = 0.0;
        public double toothInnerDiameter { get; set; } = 0.0;
        public double toothHeight { get; set; } = 0.0;

        /// <summary>Seal-side support inner DIAMETER. Calculators use /2 as the
        /// cylinder radius R in the structural lambda.</summary>
        public double supportInnerDiameter { get; set; } = 0.0;

        /// <summary>Area A1 — weights the SEAL-side speed in the acoustic delta.</summary>
        public double areaA1 { get; set; } = 0.0;

        /// <summary>Area A2 — weights the COUNTERPART-side speed in the acoustic delta.</summary>
        public double areaA2 { get; set; } = 0.0;

        /// <summary>Teeth pitch Pt — axial acoustic wavelength is 2·Pt/mode.</summary>
        public double teethPt { get; set; } = 0.0;

        /// <summary>Groove centroid radius Rc — circumferential acoustic
        /// wavelength is 2π·Rc/nd. This one IS a radius, as named.</summary>
        public double grooveCentroidRc { get; set; } = 0.0;

        /// <summary>Seal-side first-to-last tooth length l (structural lambda).</summary>
        public double firstToLastLength { get; set; } = 0.0;

        /// <summary>Counterpart inner DIAMETER. Calculators use /2 as R.</summary>
        public double counterpartInnerDiameter { get; set; } = 0.0;

        /// <summary>Counterpart first-to-last length l.</summary>
        public double counterpartFirstToLastLength { get; set; } = 0.0;

        /// <summary>Honeycomb cell geometry — only meaningful when
        /// CategoricalParameters.Coating == CounterpartCoating.Honeycomb.</summary>
        public HoneyCombParameters honeyCombParameters { get; set; }

        public GeometricParameters()
        {
            honeyCombParameters = new HoneyCombParameters()
            {
                Parent = this
            };
            Children.Add(honeyCombParameters);
        }
    }
}
