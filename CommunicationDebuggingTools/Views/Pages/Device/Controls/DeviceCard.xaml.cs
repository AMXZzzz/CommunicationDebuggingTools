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
    /// DeviceCard.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceCard : System.Windows.Controls.UserControl {

        public event Action EditClicked;
        public DeviceCard () {
            InitializeComponent();

            // 编辑按钮点击 → 通知页面
            if (btnEdit != null)
                btnEdit.Click += (s, e) => EditClicked?.Invoke();
        }

        /// <summary>
        /// 设置设备状态显示（仅 UI，后续由数据绑定替代）
        /// </summary>
        public void SetStatus (string statusText, string statusType) {
            plcCurrentState.Text = statusText;

            System.Windows.Media.Brush brush;
            switch (statusType) {
                case "Success":
                    brush = (System.Windows.Media.Brush)FindResource("SF.Brush.Status.Success");
                    btnPrimary.Content = "断开";
                    btnPrimary.Style = (Style)FindResource("SF.Style.DangerButton");
                    break;
                case "Warning":
                    brush = (System.Windows.Media.Brush)FindResource("SF.Brush.Status.Warning");
                    btnPrimary.Content = "取消";
                    btnPrimary.Style = (Style)FindResource("SF.Style.DangerButton");
                    break;
                case "Error":
                    brush = (System.Windows.Media.Brush)FindResource("SF.Brush.Status.Error");
                    btnPrimary.Content = "重连";
                    btnPrimary.Style = (Style)FindResource("SF.Style.PrimaryButton");
                    break;
                default: // Offline
                    brush = (System.Windows.Media.Brush)FindResource("SF.Brush.Text.Secondary");
                    btnPrimary.Content = "连接";
                    btnPrimary.Style = (Style)FindResource("SF.Style.PrimaryButton");
                    break;
            }

            plcStatusLight.Fill = brush;
            plcCurrentState.Foreground = brush;
            AccentBar.Background = brush;
        }

        /// <summary>
        /// 设置设备名称显示（仅 UI，后续由数据绑定替代）
        /// </summary>
        /// <param name="deviceName"></param>
        public void SetDeviceName (string deviceName) {
            devicenName.Text = deviceName;
        }


        /// <summary>
        /// 设置设备型号显示（仅 UI，后续由数据绑定替代）
        /// </summary>
        /// <param name="deviceType"></param>
        public void SetPlcModelName (string deviceType) {
            plcModelName.Text = deviceType;
        }
    }
}
