using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoLogin.ViewModels
{
    /// <summary>
    /// 轻量 INotifyPropertyChanged 基类（不引入第三方 MVVM 框架，保持单文件零依赖）。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null && propertyName != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
