using System.ComponentModel;

namespace Orva.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}