using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备管理页面。
    /// <para>
    /// 列表：设备卡片 + 末尾添加卡；编辑：遮罩 + Popup 模态；
    /// 工具栏：<see cref="DeviceToolBar"/>；
    /// 删除：点「删除」进入多选（显示 CheckBox）→「确认删除」/「取消」。
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

        /// <summary>
        /// 初始化列表、订阅集合/编辑面板/工具栏，并在 Unloaded 时退订。
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

        /// <summary>卸下页面时取消全局与工具栏订阅，防止泄漏。</summary>
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

        /// <summary>业务集合变化 → 重建展示列表。</summary>
        private void Devices_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) {
            RebuildDisplayList();
        }

        /// <summary>
        /// 重建列表并刷新数量；若在多选中，布局完成后重新显示勾选框。
        /// </summary>
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

        /// <summary>更新工具栏设备总数。</summary>
        private void RefreshCount () {
            if (toolBar != null)
                toolBar.SetCount(MyAppServices.Devices.Devices.Count);
        }

        /// <summary>
        /// 进入/退出多选：切换工具栏按钮，并显示或隐藏各卡片 CheckBox。
        /// </summary>
        private void SetSelectMode (bool on) {
            _selectMode = on;

            if (toolBar != null)
                toolBar.SetSelectMode(on);

            ApplySelectModeToCards(on);
        }

        /// <summary>对当前可视树中的 DeviceCard 应用多选外观。</summary>
        private void ApplySelectModeToCards (bool on) {
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList))
                card.SetSelectionMode(on);
        }

        /// <summary>
        /// 工具栏「删除」：仅进入多选模式，不执行删除。
        /// </summary>
        private void OnDeleteSelected () {
            if (!_selectMode)
                SetSelectMode(true);
        }

        /// <summary>
        /// 工具栏「确认删除」：删除所有已勾选设备，然后退出多选。
        /// </summary>
        private void OnConfirmDelete () {
            var ids = new List<string>();

            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList)) {
                if (card.IsSelected && card.Device != null && !string.IsNullOrEmpty(card.Device.Id))
                    ids.Add(card.Device.Id);
            }

            foreach (string id in ids) {
                try {
                    MyAppServices.Devices.Remove(id);
                } catch {
                }
            }

            SetSelectMode(false);
        }

        /// <summary>工具栏「取消」：退出多选。</summary>
        private void OnCancelSelect () {
            SetSelectMode(false);
        }

        /// <summary>打开添加设备弹窗。</summary>
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

        /// <summary>打开编辑设备弹窗。</summary>
        public void OpenEditDevice (DeviceInfo info) {
            if (info == null)
                return;
            editPanel.LoadData(info, false);
            ShowEditPopup();
        }

        /// <summary>显示遮罩与编辑 Popup。</summary>
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

        /// <summary>关闭编辑 Popup 并隐藏遮罩。</summary>
        private void CloseEditPopup () {
            editPopup.IsOpen = false;
            if (editMask != null)
                editMask.Visibility = Visibility.Collapsed;
        }

        /// <summary>保存：校验名称、协议后 Add 或 Update。</summary>
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
            } catch {
            }

            CloseEditPopup();
        }

        /// <summary>编辑面板内删除当前设备。</summary>
        private void EditPanel_DeleteRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();
            if (!editPanel.IsNew && info != null && !string.IsNullOrEmpty(info.Id)) {
                try {
                    MyAppServices.Devices.Remove(info.Id);
                } catch {
                }
            }
            CloseEditPopup();
        }

        /// <summary>一键连接所有未连接设备。</summary>
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

        /// <summary>一键断开全部设备。</summary>
        private void OnDisconnectAll () {
            foreach (DeviceInfo d in MyAppServices.Devices.Devices.ToList()) {
                if (d == null || string.IsNullOrEmpty(d.Id))
                    continue;
                MyAppServices.Devices.Disconnect(d.Id);
            }
        }

        /// <summary>从磁盘重新加载设备列表。</summary>
        private void OnRefresh () {
            MyAppServices.Devices.Load();
            RebuildDisplayList();
        }

        /// <summary>
        /// 在可视树中查找指定类型的子元素（用于收集 DeviceCard）。
        /// </summary>
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