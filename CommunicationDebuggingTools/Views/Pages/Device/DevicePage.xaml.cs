using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备管理页面。
    /// <para>
    /// 职责：展示设备卡片列表（末尾附带「添加设备」占位卡），
    /// 通过弹出式编辑面板完成新增 / 编辑 / 删除；
    /// 工具栏提供一键连接、一键断开、刷新等操作。
    /// </para>
    /// <para>
    /// 刷新策略：
    /// - 集合增删（Add/Remove/Load）→ <see cref="RebuildDisplayList"/> 重建列表；
    /// - 连接状态等属性变化 → 依赖 <see cref="DeviceInfo"/> 的 INotifyPropertyChanged，
    ///   由 <see cref="DeviceCard"/> 自行刷新，避免整表重建导致闪烁。
    /// </para>
    /// <para>
    /// 编辑交互：<see cref="ShowEditPopup"/> 显示遮罩 + Popup（模态，仅面板可点）；
    /// 所有关闭路径统一走 <see cref="CloseEditPopup"/>，避免遮罩残留。
    /// </para>
    /// </summary>
    public partial class DevicePage : Page {
        /// <summary>
        /// 绑定到 ItemsControl 的展示集合。
        /// 内容为：全部 <see cref="DeviceInfo"/> + 末尾一个 <see cref="AddDeviceMarker"/>。
        /// 与业务层 <c>MyAppServices.Devices.Devices</c> 解耦，便于按类型选择 DataTemplate。
        /// </summary>
        private readonly ObservableCollection<object> _displayList =
            new ObservableCollection<object>();

        /// <summary>
        /// 初始化页面：绑定列表、首次构建展示数据、订阅集合变化与编辑面板事件。
        /// </summary>
        public DevicePage () {
            InitializeComponent();

            deviceList.ItemsSource = _displayList;
            RebuildDisplayList();

            MyAppServices.Devices.Devices.CollectionChanged += Devices_CollectionChanged;

            if (editPanel != null) {
                editPanel.CloseRequested += () => CloseEditPopup();
                editPanel.SaveRequested += EditPanel_SaveRequested;
                editPanel.DeleteRequested += EditPanel_DeleteRequested;
            }

            // 页面从 Frame 卸下时取消全局订阅
            Unloaded += DevicePage_Unloaded;
        }

        /// <summary>
        /// 取消全局订阅，避免页面被卸下后仍然响应业务层事件。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DevicePage_Unloaded (object sender, RoutedEventArgs e) {
            MyAppServices.Devices.Devices.CollectionChanged -= Devices_CollectionChanged;
            Unloaded -= DevicePage_Unloaded;
        }

        /// <summary>
        /// 业务设备集合发生变化（增/删/Reset）时重建展示列表。
        /// </summary>
        private void Devices_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) {
            RebuildDisplayList();
        }

        /// <summary>
        /// 重建展示列表：清空后写入全部设备，再追加「添加」占位项，并刷新数量角标。
        /// 应只在设备数量变化或从磁盘重新 Load 时调用。
        /// </summary>
        private void RebuildDisplayList () {
            _displayList.Clear();

            foreach (DeviceInfo d in MyAppServices.Devices.Devices)
                _displayList.Add(d);

            _displayList.Add(AddDeviceMarker.Instance);
            RefreshCount();
        }

        /// <summary>
        /// 刷新「设备总数」文本（不含添加占位卡）。
        /// </summary>
        private void RefreshCount () {
            if (deviceCountText != null)
                deviceCountText.Text = MyAppServices.Devices.Devices.Count.ToString();
        }

        /// <summary>
        /// 打开「添加设备」弹窗（由 <see cref="AddDeviceCard"/> 点击时调用）。
        /// </summary>
        public void OpenAddDevice () {
            DeviceInfo blank = new DeviceInfo
            {
                Name = "",
                Model = "",
                Protocol = "Modbus TCP"
            };
            editPanel.LoadData(blank, true);
            ShowEditPopup();
        }

        /// <summary>
        /// 打开「编辑设备」弹窗（由 <see cref="DeviceCard"/> 编辑按钮调用）。
        /// </summary>
        /// <param name="info">当前卡片对应的设备数据，不能为 null。</param>
        public void OpenEditDevice (DeviceInfo info) {
            if (info == null)
                return;

            editPanel.LoadData(info, false);
            ShowEditPopup();
        }

        /// <summary>
        /// 显示编辑层：遮罩（挡住页面其它区域）+ 居中 Popup。
        /// 使用 Dispatcher 延迟打开，避免在 MouseUp 同一周期内打开后被立刻关闭。
        /// </summary>
        private void ShowEditPopup () {
            Window owner = Window.GetWindow(this);
            if (owner != null)
                editPopup.PlacementTarget = owner;

            editPopup.Placement = PlacementMode.Center;

            if (editMask != null)
                editMask.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(() => {
                editPopup.IsOpen = true;
            }), DispatcherPriority.Input);
        }

        /// <summary>
        /// 关闭编辑 Popup 并隐藏遮罩。
        /// 取消、保存、删除等所有关窗入口必须调用本方法，禁止只写 editPopup.IsOpen = false。
        /// </summary>
        private void CloseEditPopup () {
            editPopup.IsOpen = false;
            if (editMask != null)
                editMask.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 编辑面板「保存」：校验名称后调用业务 Add 或 Update，然后关闭弹窗。
        /// 集合变更会触发 <see cref="Devices_CollectionChanged"/> 从而刷新列表。
        /// </summary>
        private void EditPanel_SaveRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();

            // 名称为空：直接关窗（不弹 MessageBox）
            if (string.IsNullOrWhiteSpace(info.Name)) {
                CloseEditPopup();
                return;
            }

            try {
                if (editPanel.IsNew)
                    MyAppServices.Devices.Add(info);
                else
                    MyAppServices.Devices.Update(info);
            } catch {
                // 设备已删除等异常：忽略，仍关闭面板
            }

            CloseEditPopup();
        }

        /// <summary>
        /// 编辑面板「删除设备」：调用业务 Remove 后关闭弹窗（无二次确认）。
        /// </summary>
        private void EditPanel_DeleteRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();

            if (!editPanel.IsNew && info != null && !string.IsNullOrEmpty(info.Id)) {
                try {
                    MyAppServices.Devices.Remove(info.Id);
                } catch {
                    // 忽略
                }
            }

            CloseEditPopup();
        }

        /// <summary>
        /// 工具栏「一键连接」：对所有未连接设备依次异步连接。
        /// 只修改各 <see cref="DeviceInfo"/> 属性，不重建列表，由卡片响应属性通知。
        /// </summary>
        private async void BtnConnectAll_Click (object sender, RoutedEventArgs e) {
            var list = MyAppServices.Devices.Devices
                .Where(d => d != null && !d.IsConnected)
                .ToList();

            foreach (DeviceInfo d in list) {
                d.StatusType = DeviceStatusType.Connecting;
                d.IsConnected = false;
            }

            foreach (DeviceInfo d in list) {
                try {
                    await MyAppServices.Devices.ConnectAsync(d.Id, CancellationToken.None);
                } catch {
                    d.IsConnected = false;
                    d.StatusType = DeviceStatusType.Error;
                }
            }
        }

        /// <summary>
        /// 工具栏「一键断开」：断开全部已管理设备的会话。
        /// 状态变更由 DeviceInfo 通知卡片，无需 RebuildDisplayList。
        /// </summary>
        private void BtnDisconnectAll_Click (object sender, RoutedEventArgs e) {
            foreach (DeviceInfo d in MyAppServices.Devices.Devices.ToList()) {
                if (d == null || string.IsNullOrEmpty(d.Id))
                    continue;
                MyAppServices.Devices.Disconnect(d.Id);
            }
        }

        /// <summary>
        /// 工具栏「刷新」：从持久化重新加载设备列表（连接状态会按业务规则重置为离线）。
        /// </summary>
        private void BtnRefresh_Click (object sender, RoutedEventArgs e) {
            MyAppServices.Devices.Load();
            RebuildDisplayList();
        }

        /// <summary>
        /// 工具栏「删除」：尚未实现多选，提示用户走卡片编辑面板中的删除。
        /// </summary>
        private void BtnDeleteSelected_Click (object sender, RoutedEventArgs e) {
            MessageBox.Show(
                "请先在设备卡片中点「编辑」再删除。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}