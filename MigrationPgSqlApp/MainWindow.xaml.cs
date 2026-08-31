using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using MigrationPgSqlApp.ViewModels;

namespace MigrationPgSqlApp
{
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            
            // Populate password boxes with loaded config values
            OraPasswordBox.Password = ViewModel.ConnectionVM.OraPassword;
            PgPasswordBox.Password = ViewModel.ConnectionVM.PgPassword;
            if (SrcPgPasswordBox != null)
                SrcPgPasswordBox.Password = ViewModel.ConnectionVM.SrcPgPassword;
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private void OraPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.ConnectionVM.OraPassword = passwordBox.Password;
            }
        }

        private void SrcPgPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.ConnectionVM.SrcPgPassword = passwordBox.Password;
            }
        }

        private void PgPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                ViewModel.ConnectionVM.PgPassword = passwordBox.Password;
            }
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
