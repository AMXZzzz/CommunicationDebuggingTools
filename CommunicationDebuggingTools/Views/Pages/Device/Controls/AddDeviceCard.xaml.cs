using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    public partial class AddDeviceCard : UserControl {
        public AddDeviceCard () {
            InitializeComponent();
            Cursor = System.Windows.Input.Cursors.Hand;
            MouseLeftButtonUp += AddDeviceCard_MouseLeftButtonUp;
        }

        private void AddDeviceCard_MouseLeftButtonUp (object sender, MouseButtonEventArgs e) {
            DevicePage page = FindParentPage(this);
            if (page != null)
                page.OpenAddDevice();
        }

        private static DevicePage FindParentPage (DependencyObject d) {
            while (d != null) {
                DevicePage p = d as DevicePage;
                if (p != null)
                    return p;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}