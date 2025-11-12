using System.Windows;
using System.Windows.Input;

namespace SecureExamPlatform.UI
{
    public partial class ExitPasswordDialog : Window
    {
        public string EnteredPassword { get; private set; }

        public ExitPasswordDialog()
        {
            InitializeComponent();

            PasswordBox.Focus();
            PasswordBox.KeyDown += PasswordBox_KeyDown;
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelButton_Click(sender, e);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
