using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LakeCore
{
    public interface IDataModel
    {
        string Id { get; set; }
        string Name { get; set; }
        IDataModel Parent { get; set; }
        LakeComponentType ToothType { get; set; }
        IDataModelCollection Children { get; set; }


        TreeNodeValidationState Validation { get; set; }
    }

    public enum LakeComponentType
    {
        Project,
        Seal,
        SealCriteria,
        CategoricalParametries,
        Geometry,
        EngineConfig,
        Conditions,
        Results,
        ModalAnalysis,
        TaperedTooth,
        HPCTooth,
        SymmetricTooth,
        HoneycombParams,
        SealNodalFrequencyData,
        CounterpartNodalFrequencyData,
        NA
    }

    public enum TreeNodeValidationState
    {
        None,
        Valid,
        Invalid,
        lightning,
        settings,
        settings2,
        warning
    }
}
