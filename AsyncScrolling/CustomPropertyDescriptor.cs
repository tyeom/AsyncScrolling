using System;
using System.ComponentModel;

namespace AsyncScrolling
{
    /// <summary>
    /// 수동으로 속성 정보를 제공한다.
    /// 컨트롤 바인딩시 CLR에서 참조되도록 되어 있다.
    /// </summary>
    /// <typeparam name="T">속성 타입</typeparam>
    public class CustomPropertyDescriptor<T> : PropertyDescriptor
    {
        #region Member Fields
        private Type propertyType;
        private Type componentType;
        private T propertyValue;
        #endregion

        #region Constructor
        public CustomPropertyDescriptor(string propertyName, Type componentType)
            : base(propertyName, new Attribute[] { })
        {
            this.propertyType = typeof(T);
            this.componentType = componentType;
        }
        #endregion

        #region PropertyDescriptor Implementation Overriden Methods
        public override bool CanResetValue(object component) { return true; }
        public override Type ComponentType { get { return componentType; } }

        public override object GetValue(object component)
        {
            return propertyValue;
        }

        public override bool IsReadOnly { get { return false; } }
        public override Type PropertyType { get { return propertyType; } }
        public override void ResetValue(object component) { SetValue(component, default(T)); }
        public override void SetValue(object component, object value)
        {
            if (!value.GetType().IsAssignableFrom(propertyType))
            {
                throw new System.Exception("Invalid type to assign");
            }

            propertyValue = (T)value;
        }

        public override bool ShouldSerializeValue(object component) { return true; }
        #endregion
    }
}
