/*
 * SealCriteria — root input node for the criteria-check engine and the hub
 * the UI tree binds to.
 *
 * One seal = two sides: the seal (teeth) side and the counterpart
 * (stator / rotor / counter-rotor). Each side has its own modal data
 * (NodalFrequencyData) holding Assembly (cold) and Redline (hot) tables.
 *
 * Changes vs the original program:
 *   - HoneyCombParameters no longer hangs directly off SealCriteria; its
 *     single home is GeometricParameters.honeyCombParameters.
 *   - Calculations live in LakeCore.CriteriaChecks (CriteriaCheckEngine.Run
 *     takes a SealCriteria and returns a CriteriaResults); this class is
 *     pure input/tree state.
 */
using System;

namespace LakeCore
{
    public class SealCriteria : LakeComponent
    {
        public override string Name { get; set; } = "Seal Criteria";
        public override LakeComponentType ToothType { get; set; } = LakeComponentType.SealCriteria;

        public CategoricalParameters CategoricalParameters { get; set; }
        public GeometricParameters GeometricParameters { get; set; }
        public Conditions Conditions { get; set; }
        public NodalFrequencyData SealNodalFrequencyData { get; set; }
        public NodalFrequencyData CounterpartNodalFrequencyData { get; set; }

        public SealCriteria()
        {
            CategoricalParameters = new CategoricalParameters()
            {
                Parent = this
            };
            GeometricParameters = new GeometricParameters()
            {
                Parent = this
            };
            Conditions = new Conditions()
            {
                Parent = this
            };
            SealNodalFrequencyData = new NodalFrequencyData()
            {
                Name = "Seal Nodal Frequency Data",
                ToothType = LakeComponentType.SealNodalFrequencyData,
                Parent = this
            };
            CounterpartNodalFrequencyData = new NodalFrequencyData()
            {
                Name = "Counterpart Nodal Frequency Data",
                ToothType = LakeComponentType.CounterpartNodalFrequencyData,
                Parent = this
            };

            Children.Add(CategoricalParameters);
            Children.Add(GeometricParameters);
            Children.Add(Conditions);
            Children.Add(SealNodalFrequencyData);
            Children.Add(CounterpartNodalFrequencyData);
        }
    }
}
