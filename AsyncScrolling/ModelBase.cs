using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;

namespace AsyncScrolling
{
    public class ModelBase : CustomTypeDescriptor, INotifyPropertyChanged
    {
        #region Member Fields
        /// <summary>
        /// 모델에서 사용되는 속성 리스트
        /// 주로 바인딩되는 속성들이다.
        /// </summary>
        List<PropertyDescriptor> _properties = new List<PropertyDescriptor>();
        #endregion

        #region Constructor
        public ModelBase() { }
        #endregion

        #region INotifyPropertyChange Implementation
        public event PropertyChangedEventHandler PropertyChanged = delegate { };
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion INotifyPropertyChange Implementation

        #region Public Methods
        /// <summary>
        /// 속성 설정
        /// </summary>
        /// <typeparam name="T">속성 타입</typeparam>
        /// <param name="propertyName">속성 이름</param>
        /// <param name="propertyValue">속성 값</param>
        public void SetPropertyValue<T>(string propertyName, T propertyValue)
        {
            var properties = this.GetProperties()
                                    .Cast<PropertyDescriptor>()
                                    .Where(prop => prop.Name.Equals(propertyName));

            if (properties == null || properties.Count() != 1)
            {
                throw new Exception("The property doesn't exist.");
            }

            var property = properties.First();
            property.SetValue(this, propertyValue);

            OnPropertyChanged(propertyName);
        }

        /// <summary>
        /// 속성 값 불러오기
        /// </summary>
        /// <typeparam name="T">속성 타입</typeparam>
        /// <param name="propertyName">속성 이름</param>
        /// <returns></returns>
        public T GetPropertyValue<T>(string propertyName)
        {
            var properties = this.GetProperties()
                                .Cast<PropertyDescriptor>()
                                .Where(prop => prop.Name.Equals(propertyName));

            if (properties == null || properties.Count() != 1)
            {
                throw new Exception("The property doesn't exist.");
            }

            var property = properties.First();
            return (T)property.GetValue(this);
        }

        /// <summary>
        /// 속성 추가
        /// </summary>
        /// <typeparam name="T">속성 타입</typeparam>
        /// <typeparam name="U">속성 Onwer (모델)</typeparam>
        /// <param name="propertyName"></param>
        public void AddProperty<T, U>(string propertyName) where U : ModelBase
        {
            var customProperty =
                    new CustomPropertyDescriptor<T>(
                                            propertyName,
                                            typeof(U));

            _properties.Add(customProperty);
            customProperty.AddValueChanged(
                                        this,
                                        (o, e) => { OnPropertyChanged(propertyName); });
        }
        #endregion

        #region CustomTypeDescriptor Implementation Overriden Methods
        public override PropertyDescriptorCollection GetProperties()
        {
            var properties = base.GetProperties();
            return new PropertyDescriptorCollection(
                                properties.Cast<PropertyDescriptor>()
                                          .Concat(_properties).ToArray());
        }
        #endregion
    }
}
