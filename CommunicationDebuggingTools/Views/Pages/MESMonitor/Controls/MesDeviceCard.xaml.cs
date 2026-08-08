using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommunicationDebuggingTools.Views.Pages.MESMonitor.Controls {
    public partial class MesDeviceCard : UserControl {
        public MesDeviceCard () {
            InitializeComponent();
        }

        /// <summary>
        /// 设置卡片状态（仅 UI 演示，后续用绑定替代）
        /// </summary>
        public void SetStatus (string statusText, string statusType) {
            txtStatus.Text = statusText;

            Brush bg;
            Brush fg;

            switch (statusType) {
                case "Success": // RUN
                    bg = (Brush)FindResource("SF.Brush.Mes.LiveBg");
                    fg = (Brush)FindResource("SF.Brush.Mes.LiveFg");
                    break;
                case "Error":   // ALARM
                    bg = (Brush)FindResource("SF.Brush.Mes.AlarmBg");
                    fg = (Brush)FindResource("SF.Brush.Mes.AlarmFg");
                    break;
                default:        // 离线
                    bg = (Brush)FindResource("SF.Brush.Bg.Hover");
                    fg = (Brush)FindResource("SF.Brush.Text.Secondary");
                    break;
            }

            statusBadge.Background = bg;
            txtStatus.Foreground = fg;
        }

        /// <summary>
        /// 设置基本信息（名称、副标题、轨道类型）
        /// </summary>
        public void SetInfo (string name, string sub, string lane, bool isDual) {
            txtName.Text = name;
            txtSub.Text = sub;
            txtLane.Text = lane;

            if (isDual) {
                laneBadge.Background = (Brush)FindResource("SF.Brush.Mes.TagDualBg");
                txtLane.Foreground = (Brush)FindResource("SF.Brush.Mes.WidthTitle");
            } else {
                laneBadge.Background = (Brush)FindResource("SF.Brush.Mes.TagSingleBg");
                txtLane.Foreground = (Brush)FindResource("SF.Brush.Mes.TagSingleFg");
            }
        }
    }
}