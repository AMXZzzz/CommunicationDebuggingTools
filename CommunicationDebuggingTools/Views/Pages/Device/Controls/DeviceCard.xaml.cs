using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// PLC 设备卡片。
    /// 通过依赖属性 <see cref="Device"/> 接收数据；文本类字段由 XAML 绑定，
    /// 状态灯 / 主按钮样式由 <see cref="ApplyStatusVisual"/> 根据 StatusType 更新。
    /// </summary>
    public partial class DeviceCard : UserControl {
        /// <summary>当前订阅了 PropertyChanged 的设备（用于取消旧订阅）。</summary>
        private DeviceInfo _subscribed;

        /// <summary>绑定到卡片的设备数据。</summary>
        public static readonly DependencyProperty DeviceProperty =
            DependencyProperty.Register(
                "Device",
                typeof(DeviceInfo),
                typeof(DeviceCard),
                new PropertyMetadata(null, OnDeviceChanged));

        /// <summary>当前设备；与 DataContext 同步，供按钮事件使用。</summary>
        public DeviceInfo Device {
            get { return (DeviceInfo)GetValue(DeviceProperty); }
            set { SetValue(DeviceProperty, value); }
        }

        public DeviceCard () {
            InitializeComponent();

            if (btnEdit != null)
                btnEdit.Click += BtnEdit_Click;

            if (btnPrimary != null)
                btnPrimary.Click += BtnPrimary_Click;

            Unloaded += DeviceCard_Unloaded;
        }

        /// <summary>
        /// 卡片从可视树移除时取消对 DeviceInfo 的订阅，避免旧卡片被属性通知拖住。
        /// </summary>
        private void DeviceCard_Unloaded (object sender, RoutedEventArgs e) {
            if (_subscribed != null) {
                _subscribed.PropertyChanged -= Device_PropertyChanged;
                _subscribed = null;
            }

            Unloaded -= DeviceCard_Unloaded;
        }
        /// <summary>
        /// Device 依赖属性变更：设置 DataContext 供 {Binding}，并订阅状态通知。
        /// </summary>
        private static void OnDeviceChanged (DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var card = (DeviceCard)d;
            card.DataContext = e.NewValue;
            card.ApplyDevice(e.NewValue as DeviceInfo);
        }

        /// <summary>
        /// 切换数据源：取消旧订阅、订阅新设备，并刷新状态外观。
        /// </summary>
        private void ApplyDevice (DeviceInfo info) {
            if (_subscribed != null)
                _subscribed.PropertyChanged -= Device_PropertyChanged;

            _subscribed = info;

            if (info != null) {
                info.PropertyChanged += Device_PropertyChanged;
                ApplyStatusVisual(info.StatusType);
            }
        }

        /// <summary>
        /// 设备属性变化时，仅在状态相关字段变化时更新灯与主按钮。
        /// Name/Ip 等已由绑定自动刷新，无需整卡重绑。
        /// </summary>
        private void Device_PropertyChanged (object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == "StatusType" ||
                e.PropertyName == "StatusText" ||
                e.PropertyName == "IsConnected") {
                if (Device != null)
                    ApplyStatusVisual(Device.StatusType);
            }
        }

        /// <summary>强制按当前 Device 刷新状态外观（一般可依赖 PropertyChanged）。</summary>
        public void RefreshFromDevice () {
            ApplyDevice(Device);
        }

        /// <summary>
        /// 将 StatusType 映射为文案 Key，再交给 SetStatus 更新灯、色条、主按钮。
        /// </summary>
        private void ApplyStatusVisual (DeviceStatusType type) {
            string statusKey;

            switch (type) {
                case DeviceStatusType.Success:
                    statusKey = "Success";
                    break;
                case DeviceStatusType.Connecting:
                    statusKey = "Warning";
                    break;
                case DeviceStatusType.Warning:
                    statusKey = "Warning";
                    break;
                case DeviceStatusType.Error:
                    statusKey = "Error";
                    break;
                default:
                    statusKey = "Offline";
                    break;
            }

            // 文案以绑定 StatusText 为准；这里再写一次保证未绑定时也有显示
            string statusText = Device != null ? Device.StatusText : "离线";
            SetStatus(statusText, statusKey);
        }

        /// <summary>
        /// 更新状态文字颜色、状态灯、左侧色条、主按钮 Content/Style。
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
                        btnPrimary.Style = (Style)FindResource("SF.Style.DangerButton");
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

        /// <summary>打开编辑弹窗。</summary>
        private void BtnEdit_Click (object sender, RoutedEventArgs e) {
            DeviceInfo info = Device;
            if (info == null)
                return;

            DevicePage page = FindParentPage(this);
            if (page != null)
                page.OpenEditDevice(info);
        }

        /// <summary>
        /// 连接 / 断开 / 重连。
        /// 连接使用 ConnectAsync，不阻塞 UI；状态变化通过 PropertyChanged 刷新外观。
        /// </summary>
        private async void BtnPrimary_Click (object sender, RoutedEventArgs e) {
            DeviceInfo info = Device;
            if (info == null || string.IsNullOrEmpty(info.Id))
                return;

            // 已连接或连接中 → 断开
            if (info.IsConnected || info.StatusType == DeviceStatusType.Connecting) {
                MyAppServices.Devices.Disconnect(info.Id);
                return;
            }

            // 先进入连接中（触发绑定与本卡订阅）
            info.StatusType = DeviceStatusType.Connecting;
            info.IsConnected = false;

            if (btnPrimary != null)
                btnPrimary.IsEnabled = false;

            string id = info.Id;

            try {
                await MyAppServices.Devices.ConnectAsync(id, CancellationToken.None);
            } catch {
                if (Device != null && Device.Id == id) {
                    Device.IsConnected = false;
                    Device.StatusType = DeviceStatusType.Error;
                }
            } finally {
                if (btnPrimary != null)
                    btnPrimary.IsEnabled = true;
            }
        }

        /// <summary>
        /// 沿可视化树向上查找所属 <see cref="DevicePage"/>。
        /// </summary>
        private static DevicePage FindParentPage (DependencyObject d) {
            while (d != null) {
                DevicePage page = d as DevicePage;
                if (page != null)
                    return page;

                DependencyObject parent = VisualTreeHelper.GetParent(d);
                if (parent == null) {
                    FrameworkElement fe = d as FrameworkElement;
                    if (fe != null)
                        parent = fe.Parent as DependencyObject;
                }
                d = parent;
            }
            return null;
        }
    }
}