using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备管理页面。
    /// <para>
    /// 列表：设备卡片 + 末尾添加卡；编辑：遮罩 + <see cref="Visibility"/> 居中面板（随主窗口移动）；
    /// 工具栏：<see cref="DeviceToolBar"/>；
    /// 删除：点「删除」进入多选 →「确认删除」/「取消」。
    /// </para>
    /// <para>
    /// 集合增删走 <see cref="RebuildDisplayList"/>；连接状态靠 DeviceInfo 属性通知刷新卡片。
    /// </para>
    /// </summary>
    public partial class DevicePage : Page {
        /// <summary>展示集合：DeviceInfo + 末尾 AddDeviceMarker。</summary>
        private readonly ObservableCollection<object> _displayList =
            new ObservableCollection<object>();

        /// <summary>是否处于多选删除模式。</summary>
        private bool _selectMode;

        public DevicePage () {
            InitializeComponent();

            deviceList.ItemsSource = _displayList;
            RebuildDisplayList();

            MyAppServices.Devices.Devices.CollectionChanged += Devices_CollectionChanged;

            if (editPanel != null) {
                editPanel.CloseRequested += CloseEditPopup;
                editPanel.SaveRequested += EditPanel_SaveRequested;
                editPanel.DeleteRequested += EditPanel_DeleteRequested;
            }

            if (toolBar != null) {
                toolBar.ConnectAllClicked += OnConnectAll;
                toolBar.DisconnectAllClicked += OnDisconnectAll;
                toolBar.RefreshClicked += OnRefresh;
                toolBar.DeleteClicked += OnDeleteSelected;
                toolBar.ConfirmDeleteClicked += OnConfirmDelete;
                toolBar.CancelSelectClicked += OnCancelSelect;
            }

            Unloaded += DevicePage_Unloaded;
        }

        /// <summary>卸下页面时退订，避免泄漏。</summary>
        private void DevicePage_Unloaded (object sender, RoutedEventArgs e) {
            MyAppServices.Devices.Devices.CollectionChanged -= Devices_CollectionChanged;

            if (toolBar != null) {
                toolBar.ConnectAllClicked -= OnConnectAll;
                toolBar.DisconnectAllClicked -= OnDisconnectAll;
                toolBar.RefreshClicked -= OnRefresh;
                toolBar.DeleteClicked -= OnDeleteSelected;
                toolBar.ConfirmDeleteClicked -= OnConfirmDelete;
                toolBar.CancelSelectClicked -= OnCancelSelect;
            }

            Unloaded -= DevicePage_Unloaded;
        }

        private void Devices_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) =>
            RebuildDisplayList();

        /// <summary>重建展示列表；多选模式下布局完成后重新显示勾选框。</summary>
        private void RebuildDisplayList () {
            _displayList.Clear();

            foreach (DeviceInfo d in MyAppServices.Devices.Devices)
                _displayList.Add(d);

            _displayList.Add(AddDeviceMarker.Instance);
            RefreshCount();

            if (_selectMode) {
                Dispatcher.BeginInvoke(new Action(() => {
                    ApplySelectModeToCards(true);
                }), DispatcherPriority.Loaded);
            }
        }

        private void RefreshCount () {
            if (toolBar != null)
                toolBar.SetCount(MyAppServices.Devices.Devices.Count);
        }

        private void SetSelectMode (bool on) {
            _selectMode = on;
            if (toolBar != null)
                toolBar.SetSelectMode(on);
            ApplySelectModeToCards(on);
        }

        private void ApplySelectModeToCards (bool on) {
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList))
                card.SetSelectionMode(on);
        }

        private void OnDeleteSelected () {
            if (!_selectMode)
                SetSelectMode(true);
        }

        private void OnConfirmDelete () {
            var ids = new List<string>();
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList)) {
                if (card.IsSelected && card.Device != null && !string.IsNullOrEmpty(card.Device.Id))
                    ids.Add(card.Device.Id);
            }

            foreach (string id in ids) {
                try { MyAppServices.Devices.Remove(id); } catch { }
            }

            SetSelectMode(false);
        }

        private void OnCancelSelect () => SetSelectMode(false);

        /// <summary>打开添加设备面板。</summary>
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

        /// <summary>打开编辑设备面板。</summary>
        public void OpenEditDevice (DeviceInfo info) {
            if (info == null) return;
            editPanel.LoadData(info, false);
            ShowEditPopup();
        }

        /// <summary>显示遮罩与编辑面板（Visibility，随主窗口移动）。</summary>
        private void ShowEditPopup () {
            if (editPanel != null)
                editPanel.Visibility = Visibility.Visible;
            if (editOverlay != null)
                editOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>关闭编辑面板并隐藏遮罩。</summary>
        private void CloseEditPopup () {
            if (editPanel != null)
                editPanel.Visibility = Visibility.Collapsed;
            if (editOverlay != null)
                editOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>点击遮罩空白处关闭。</summary>
        private void EditOverlay_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) =>
            CloseEditPopup();

        /// <summary>点击面板本身不关闭遮罩。</summary>
        private void Panel_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) =>
            e.Handled = true;

        private void EditPanel_SaveRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();

            if (string.IsNullOrWhiteSpace(info.Name) ||
                string.IsNullOrWhiteSpace(info.Protocol)) {
                CloseEditPopup();
                return;
            }

            try {
                if (editPanel.IsNew)
                    MyAppServices.Devices.Add(info);
                else
                    MyAppServices.Devices.Update(info);
            } catch { }

            CloseEditPopup();
        }

        private void EditPanel_DeleteRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();
            if (!editPanel.IsNew && info != null && !string.IsNullOrEmpty(info.Id)) {
                try { MyAppServices.Devices.Remove(info.Id); } catch { }
            }
            CloseEditPopup();
        }

        private async void OnConnectAll () {
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

        private void OnDisconnectAll () {
            foreach (DeviceInfo d in MyAppServices.Devices.Devices.ToList()) {
                if (d == null || string.IsNullOrEmpty(d.Id))
                    continue;
                MyAppServices.Devices.Disconnect(d.Id);
            }
        }

        private void OnRefresh () {
            MyAppServices.Devices.Load();
            RebuildDisplayList();
        }

        private static IEnumerable<T> FindVisualChildren<T> (DependencyObject parent)
            where T : DependencyObject {
            if (parent == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T match = child as T;
                if (match != null)
                    yield return match;

                foreach (T nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }
    }
}