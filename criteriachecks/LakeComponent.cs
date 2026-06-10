using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LakeCore
{
    public class LakeComponent : IDataModel
    {

        public virtual string Id { get; set; } = Guid.NewGuid().ToString();
        public virtual string Name { get; set; }
        public virtual IDataModel Ancestor { get; set; }
        public virtual IDataModel Parent { get; set; }
        public IDataModelCollection Children { get; set; }

        public virtual TreeNodeValidationState Validation { get; set; } = TreeNodeValidationState.None;
        public virtual LakeComponentType ToothType { get; set; }

        public LakeComponent()
        {
            Children = new IDataModelCollection(this);
        }

        public bool SetValueToProperty(string propertyName, object value)
        {
            try
            {
                PropertyInfo propertyInfo = GetType().GetProperty(propertyName);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(this, value);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
