using System.Windows;
using System.Windows.Input;

namespace Orva.Views
{
    /// <summary>
    /// Branded ORVA confirmation dialog. Replaces raw MessageBox for business
    /// confirmations. Paper card, teal grain header, scalloped ticket edge,
    /// dialog reveal. Enter = OK, Esc = cancel/close.
    /// </summary>
    public partial class OrvaConfirmDialog : Window
    {
        public OrvaConfirmDialog()
        {
            InitializeComponent();
        }

        public static bool Show(Window owner, string title, string message,
            bool isWarning, string okText, string cancelText = null)
        {
            var dialog = new OrvaConfirmDialog();
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.TitleText.Text = title ?? "ORVA";
            dialog.MessageText.Text = message ?? string.Empty;
            dialog.OkButton.Content = string.IsNullOrEmpty(okText) ? "Done" : okText;
            dialog.WarningBar.Visibility = isWarning ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrEmpty(cancelText))
            {
                dialog.CancelButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                dialog.CancelButton.Content = cancelText;
                dialog.CancelButton.Visibility = Visibility.Visible;
            }

            return dialog.ShowDialog() == true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (System.InvalidOperationException) { /* button already released */ }
            }
        }
    }
}
