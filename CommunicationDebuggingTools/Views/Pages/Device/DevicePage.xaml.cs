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

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// DevicePage.xaml 的交互逻辑
    /// </summary>
    public partial class DevicePage : Page {
        public DevicePage () {
            InitializeComponent();

            deviceCard1.SetStatus("RUN", "Success");
            deviceCard1.SetDeviceName("上板机");
            deviceCard1.SetPlcModelName("FPXH C60ET");

            deviceCard2.SetStatus("连接中...", "Warning");
            deviceCard2.SetDeviceName("印刷机");
            deviceCard2.SetPlcModelName("FPXH C60ET");

            deviceCard3.SetStatus("离线", "Offline");
            deviceCard3.SetDeviceName("SPI");
            deviceCard3.SetPlcModelName("FPXH C60ET");

            deviceCard4.SetStatus("通信超时", "Error");
            deviceCard4.SetDeviceName("贴片机");
            deviceCard4.SetPlcModelName("FPXH C60ET");

            deviceCard5.SetStatus("RUN", "Success");
            deviceCard5.SetDeviceName("回流焊");
            deviceCard5.SetPlcModelName("FPXH C60ET");

            deviceCard6.SetStatus("RUN", "Success");
            deviceCard6.SetDeviceName("AOI");
            deviceCard6.SetPlcModelName("FPXH C60ET");

            deviceCard7.SetStatus("RUN", "Success");
            deviceCard7.SetDeviceName("下板机");
            deviceCard7.SetPlcModelName("FPXH C60ET");



            deviceCard1.EditClicked += () =>
            {
                editPanel.LoadData(
                        "上板机",
                        "FPXH C60ET",
                        "Modbus TCP",
                        "192.168.0.1",
                        "502",
                        "1",
                        false,
                        "RUN");
                editPopup.PlacementTarget = Window.GetWindow(this);
                editPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                editPopup.IsOpen = true;
            };

            editPanel.CloseRequested += () => editPopup.IsOpen = false;
        }

    }
}
