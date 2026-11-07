using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AutoLogin.ViewModels;

namespace AutoLogin.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _subscribedViewModel;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Subscribe(DataContext as MainViewModel);
            ScrollLogToEnd();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Subscribe(e.NewValue as MainViewModel);
        }

        private void Subscribe(MainViewModel viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel)) return;

            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.LogEntryAdded -= OnLogEntryAdded;
            }

            _subscribedViewModel = viewModel;

            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.LogEntryAdded += OnLogEntryAdded;
            }
        }

        private void OnLogEntryAdded(object sender, EventArgs e)
        {
            ScrollLogToEnd();
        }

        private void ScrollLogToEnd()
        {
            try
            {
                if (LogList == null || LogList.Items.Count == 0) return;
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
            catch
            {
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch { }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            MainViewModel viewModel = DataContext as MainViewModel;
            if (viewModel != null && viewModel.IsRunning)
            {
                MessageBoxResult result = MessageBox.Show(
                    "自动登录正在运行，确定要退出吗？",
                    "AutoLogin",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
