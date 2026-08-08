using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// PLC 设备卡片
    /// </summary>
    public partial class DeviceCard : UserControl {
        /// <summary>点击「编辑」时通知外部</summary>
        public event Action EditClicked;

        public static readonly DependencyProperty DeviceProperty =
            DependencyProperty.Register(
                "Device",
                typeof(DeviceInfo),
                typeof(DeviceCard),
                new PropertyMetadata(null, OnDeviceChanged));

        public DeviceInfo Device {
            get { return (DeviceInfo)GetValue(DeviceProperty); }
            set { SetValue(DeviceProperty, value); }
        }

        public DeviceCard () {
            InitializeComponent();

            if (btnEdit != null)
                btnEdit.Click += (s, e) => {
                    if (EditClicked != null)
                        EditClicked();
                };
        }

        private static void OnDeviceChanged (DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var card = (DeviceCard)d;
            card.ApplyDevice(e.NewValue as DeviceInfo);
        }

        /// <summary>
        /// 根据 DeviceInfo 刷新界面
        /// </summary>
        private void ApplyDevice (DeviceInfo info) {
            if (info == null)
                return;

            SetDeviceName(info.Name ?? "");
            SetPlcModelName(info.Model ?? "");

            if (ipAddress != null)
                ipAddress.Text = info.Ip ?? "";
            if (protocolName != null)
                protocolName.Text = info.Protocol ?? "";

            ApplyStatusVisual(info.StatusType);
        }

        private void ApplyStatusVisual (DeviceStatusType type) {
            string statusText;
            string statusKey;

            switch (type) {
                case DeviceStatusType.Success:
                    statusText = "RUN";
                    statusKey = "Success";
                    break;
                case DeviceStatusType.Connecting:
                    statusText = "连接中...";
                    statusKey = "Warning";
                    break;
                case DeviceStatusType.Warning:
                    statusText = "警告";
                    statusKey = "Warning";
                    break;
                case DeviceStatusType.Error:
                    statusText = "ALARM";
                    statusKey = "Error";
                    break;
                default:
                    statusText = "离线";
                    statusKey = "Offline";
                    break;
            }

            SetStatus(statusText, statusKey);
        }

        /// <summary>
        /// 设置状态文字、指示灯、主按钮样式
        /// </summary>
        public void SetStatus (string statusText, string statusType) {
            if (plcCurrentState != null)
                plcCurrentState.Text = statusText;

            Brush brush;
            switch (statusType) {
                case "Success":
                    brush = (Brush)FindResource("SF.Brush.Status.Success");
                    if (btnPrimary != null) {
                        btnPrimary.Content = "断开";
                        btnPrimary.Style = (Style)FindResource("SF.Style.DisconnectButton");
                    }
                    break;
                case "Warning":
                    brush = (Brush)FindResource("SF.Brush.Status.Warning");
                    if (btnPrimary != null) {
                        btnPrimary.Content = "取消";
                        btnPrimary.Style = (Style)FindResource("SF.Style.DangerButton");
                    }
                    break;
                case "Error":
                    brush = (Brush)FindResource("SF.Brush.Status.Error");
                    if (btnPrimary != null) {
                        btnPrimary.Content = "重连";
                        btnPrimary.Style = (Style)FindResource("SF.Style.PrimaryButton");
                    }
                    break;
                default:
                    brush = (Brush)FindResource("SF.Brush.Text.Secondary");
                    if (btnPrimary != null) {
                        btnPrimary.Content = "连接";
                        btnPrimary.Style = (Style)FindResource("SF.Style.PrimaryButton");
                    }
                    break;
            }

            if (plcStatusLight != null)
                plcStatusLight.Fill = brush;
            if (plcCurrentState != null)
                plcCurrentState.Foreground = brush;
            if (AccentBar != null)
                AccentBar.Background = brush;
        }

        public void SetDeviceName (string deviceName) {
            if (devicenName != null)
                devicenName.Text = deviceName ?? "";
        }

        public void SetPlcModelName (string deviceType) {
            if (plcModelName != null)
                plcModelName.Text = deviceType ?? "";
        }
    }
}