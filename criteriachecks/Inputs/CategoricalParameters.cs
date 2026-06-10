/*
 * CategoricalParameters — discrete configuration choices for the seal assembly.
 * Input node (UI tree). Enum member names kept identical to the original
 * program (including "Strator") so existing UI bindings/serialized projects
 * keep working.
 */
using System;

namespace LakeCore
{
    public class CategoricalParameters : LakeComponent
    {
        public override string Name { get; set; } = "Categorical Parameters";
        public override LakeComponentType ToothType { get; set; } = LakeComponentType.CategoricalParametries;

        public SealShape SealShape { get; set; } = SealShape.Undefined;
        public CounterpartShape CounterpartShape { get; set; } = CounterpartShape.Undefined;
        public SupportType SupportType { get; set; } = SupportType.Undefined;
        public CounterpartDynamicType DynamicType { get; set; } = CounterpartDynamicType.Undefined;
        public CounterpartCoating Coating { get; set; } = CounterpartCoating.Undefined;

        public CategoricalParameters()
        {
        }

        public CategoricalParameters(SealShape sealShape, CounterpartShape counterpartShape,
            SupportType supportType, CounterpartDynamicType dynamicType, CounterpartCoating coating)
        {
            SealShape = sealShape;
            CounterpartShape = counterpartShape;
            SupportType = supportType;
            DynamicType = dynamicType;
            Coating = coating;
        }
    }

    public enum SealShape
    {
        Undefined,
        Disc,
        Cylindrical
    }

    public enum CounterpartShape
    {
        Undefined,
        Disc,
        Cylindrical
    }

    public enum SupportType
    {
        Undefined,
        OneEnd,
        BothEnds
    }

    public enum CounterpartDynamicType
    {
        Undefined,
        Rotor,
        CounterRotor,
        Strator
    }

    public enum CounterpartCoating
    {
        Undefined,
        Honeycomb,
        Weardown
    }
}
