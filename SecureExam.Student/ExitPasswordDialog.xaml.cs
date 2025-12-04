using System.Windows;
using System.Windows.Input;

namespace SecureExam.Student
{
    public partial class ExitPasswordDialog : Window
    {
        public string EnteredPassword { get; private set; }

        public ExitPasswordDialog()
        {
            InitializeComponent();
            PasswordBox.Focus();

            // Disable right-click
            this.PreviewMouseRightButtonDown += (s, e) => e.Handled = true;
            PasswordBox.PreviewMouseRightButtonDown += (s, e) => e.Handled = true;

            // Block dangerous keys
            this.PreviewKeyDown += ExitDialog_PreviewKeyDown;
        }

        private void ExitDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Block Windows key
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            // Block Alt+Tab
            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                e.Handled = true;
                return;
            }

            // Block Ctrl+combinations except text editing
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key != Key.C && e.Key != Key.V && e.Key != Key.X && e.Key != Key.A)
                {
                    e.Handled = true;
                    return;
                }
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
