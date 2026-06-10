using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LakeCore
{
    public class IDataModelCollection : List<IDataModel>
    {
        public string Name { get; set; } = "Collection";

        public TreeNodeValidationState Validation { get; set; }
        public virtual string Id { get; set; }

        public IDataModel Parent { get; set; }
        public IDataModelCollection Children { get; set; }


        public IDataModelCollection(IDataModel parent)
        {
            Parent = parent;
            Children = null;
        }
    }
}
