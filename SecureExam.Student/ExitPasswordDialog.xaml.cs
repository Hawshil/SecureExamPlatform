using System.Windows;

namespace SecureExam.Student
{
    public partial class ExitPasswordDialog : Window
    {
        public string EnteredPassword { get; private set; }

        public ExitPasswordDialog()
        {
            InitializeComponent();
            PasswordBox.Focus();
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
