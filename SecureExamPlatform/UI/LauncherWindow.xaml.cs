using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SecureExamPlatform.UI
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
        }

        private void StudentButton_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new SimplifiedLoginWindow();
            loginWindow.Show();
            this.Close();
        }

        private void FacultyButton_Click(object sender, RoutedEventArgs e)
        {
            var facultyDashboard = new FacultyDashboard();
            facultyDashboard.Show();
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
