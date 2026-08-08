using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CommunicationDebuggingTools.Views.Pages.Monitor {
    public partial class DataMonitorPage : Page {
        public DataMonitorPage () {
            InitializeComponent();

            // 卡片演示数据
            card1.SetInfo("上板机", "Siemens S7-1200 · 192.168.0.10", "单轨", false);
            card1.SetStatus("RUN", "Success");

            card2.SetInfo("AOI", "Koh Young Zenith · 192.168.0.60", "双轨", true);
            card2.SetStatus("ALARM", "Error");

            card3.SetInfo("下板机", "Omron NJ · 192.168.0.70", "单轨", false);
            card3.SetStatus("离线", "Offline");

            // 打开列表
            lineControl.AlarmClickedEvent += () =>
            {
                alarmPanel.Visibility = Visibility.Visible;
                alarmDetail.Visibility = Visibility.Collapsed;

                alarmPopup.PlacementTarget = Window.GetWindow(this);
                alarmPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                alarmPopup.IsOpen = true;
            };

            // 列表关闭
            alarmPanel.CloseRequested += () =>
            {
                alarmPopup.IsOpen = false;
            };

            // 列表 → 详情
            alarmPanel.DetailRequested += () =>
            {
                alarmPanel.Visibility = Visibility.Collapsed;
                alarmDetail.Visibility = Visibility.Visible;
            };

            // 详情 → 返回列表
            alarmDetail.BackRequested += () =>
            {
                alarmDetail.Visibility = Visibility.Collapsed;
                alarmPanel.Visibility = Visibility.Visible;
            };

            // 详情关闭
            alarmDetail.CloseRequested += () =>
            {
                alarmPopup.IsOpen = false;
            };
        }


    }
}