/*
 * HoneycombCalculator — corrects the circumferential acoustic wavelength λg
 * for a honeycomb-coated counterpart land.
 *
 * Solves for λh in:  (1/λh)·√(1 + (a/L)²·tan²(2πs/λh)) = 1/λg
 * via Brent's method (MathNet.Numerics — the only external dependency of
 * the criteria-check suite).
 *
 * Math preserved exactly from the original program.
 */
using System;
using MathNet.Numerics.RootFinding;

namespace LakeCore.CriteriaChecks
{
    public static class HoneycombCalculator
    {
        private const double AbsoluteTolerance = 1e-12;
        private const int MaxIterations = 100000;

        /// <summary>Honeycomb-corrected wavelength λh for an uncorrected
        /// acoustic wavelength λg and cell geometry (s = cavity depth,
        /// a = cell size, L = cell pitch), all in mm.</summary>
        public static double CorrectedWavelength(double a, double L, double s, double lambdaG)
        {
            double lower = 1e-6;
            double upper = 10.0 * lambdaG;

            return Brent.FindRoot(
                f: lambdaH => Residual(lambdaH, a, L, s, lambdaG),
                lowerBound: lower,
                upperBound: upper,
                accuracy: AbsoluteTolerance,
                maxIterations: MaxIterations);
        }

        private static double Residual(double lambdaH, double a, double L, double s, double lambdaG)
        {
            // Guard against division by zero / tan singularities.
            if (lambdaH <= 0.0)
                return double.PositiveInfinity;

            double term = (2.0 * Math.PI * s) / lambdaH;
            double tan = Math.Tan(term);

            // Near a tan pole: return a large residual to push the solver away.
            if (double.IsInfinity(tan) || double.IsNaN(tan))
                return double.PositiveInfinity;

            double sqrt = Math.Sqrt(1.0 + Math.Pow(a / L, 2) * tan * tan);
            return (1.0 / lambdaH) * sqrt - (1.0 / lambdaG);
        }
    }
}
