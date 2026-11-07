using System.Windows;
using System.Windows.Controls;

namespace AutoLogin.Behaviors
{
    /// <summary>
    /// 让 PasswordBox 支持 MVVM 双向绑定（Password 属性本身不是依赖属性）。
    /// 用法：
    ///   PasswordBox PasswordBoxBinding.BindPassword="True"
    ///              PasswordBoxBinding.BoundPassword="{Binding Password, Mode=TwoWay}"
    /// </summary>
    public static class PasswordBoxBinding
    {
        private static bool _isUpdating;

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxBinding),
                new PropertyMetadata(false, OnBindPasswordChanged));

        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxBinding),
                new FrameworkPropertyMetadata(
                    "",
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBoundPasswordChanged));

        public static void SetBindPassword(DependencyObject element, bool value)
        {
            element.SetValue(BindPasswordProperty, value);
        }

        public static bool GetBindPassword(DependencyObject element)
        {
            return (bool)element.GetValue(BindPasswordProperty);
        }

        public static void SetBoundPassword(DependencyObject element, string value)
        {
            element.SetValue(BoundPasswordProperty, value);
        }

        public static string GetBoundPassword(DependencyObject element)
        {
            return (string)element.GetValue(BoundPasswordProperty);
        }

        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PasswordBox box = d as PasswordBox;
            if (box == null) return;

            bool bind = (bool)e.NewValue;
            if (bind)
            {
                box.PasswordChanged -= OnPasswordChanged;
                box.PasswordChanged += OnPasswordChanged;

                string initial = GetBoundPassword(box) ?? "";
                if (box.Password != initial)
                {
                    _isUpdating = true;
                    try { box.Password = initial; }
                    finally { _isUpdating = false; }
                }
            }
            else
            {
                box.PasswordChanged -= OnPasswordChanged;
            }
        }

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PasswordBox box = d as PasswordBox;
            if (box == null || _isUpdating) return;
            if (!GetBindPassword(box)) return;

            string newValue = e.NewValue as string ?? "";
            if (box.Password == newValue) return;

            _isUpdating = true;
            try { box.Password = newValue; }
            finally { _isUpdating = false; }
        }

        private static void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordBox box = (PasswordBox)sender;
            if (_isUpdating) return;
            if (!GetBindPassword(box)) return;

            _isUpdating = true;
            try { SetBoundPassword(box, box.Password); }
            finally { _isUpdating = false; }
        }
    }
}
