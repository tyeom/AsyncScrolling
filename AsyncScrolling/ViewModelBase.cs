using System;
using System.ComponentModel;

namespace AsyncScrolling
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        #region CustomTypeDescriptor Implementation Overriden Methods
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        protected void OnPropertyChanged(string Name)
        {
            PropertyChangedEventHandler Handler = PropertyChanged;

            if (Handler != null)
            {
                Handler(this, new PropertyChangedEventArgs(Name));
            }
        }
    }
}
