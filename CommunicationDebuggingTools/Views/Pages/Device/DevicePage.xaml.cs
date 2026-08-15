using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.ViewModels;

namespace CommunicationDebuggingTools.Views.Pages.Device {

    /// <summary>
    /// 设备管理页：只负责 UI（列表模板、遮罩弹层、工具栏事件）。
    /// 业务全部委托 <see cref="DevicePageViewModel"/>（由 DI 注入）。
    /// </summary>
    public partial class DevicePage : Page {

        private readonly DevicePageViewModel _vm;

        /// <summary>DI 构造：App 中注册为 Transient。</summary>
        public DevicePage (DevicePageViewModel vm) {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            _vm = vm;
            InitializeComponent();

            DataContext = _vm;
            deviceList.ItemsSource = _vm.DisplayList;

            WireViewModel();
            WireToolbar();
            WireEditPanel();

            if (toolBar != null)
                toolBar.SetCount(_vm.DeviceCount);

            Unloaded += DevicePage_Unloaded;
        }

        // -------------------- ViewModel 事件 --------------------

        private void WireViewModel () {
            _vm.RequestOpenAdd += () => ShowEditPanel(isNew: true, null);
            _vm.RequestOpenEdit += info => ShowEditPanel(isNew: false, info);
            _vm.RequestShowError += msg =>
                MessageBox.Show(msg ?? "", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

            // 数量变化：VM 重建列表后同步工具栏
            _vm.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(DevicePageViewModel.DeviceCount) && toolBar != null)
                    toolBar.SetCount(_vm.DeviceCount);
                if (e.PropertyName == nameof(DevicePageViewModel.IsSelectMode) && toolBar != null)
                    toolBar.SetSelectMode(_vm.IsSelectMode);
            };
        }

        private void WireToolbar () {
            if (toolBar == null) return;

            toolBar.ConnectAllClicked += () => {
                if (_vm.ConnectAllCommand.CanExecute(null))
                    _vm.ConnectAllCommand.Execute(null);
            };
            toolBar.DisconnectAllClicked += () => {
                if (_vm.DisconnectAllCommand.CanExecute(null))
                    _vm.DisconnectAllCommand.Execute(null);
            };
            toolBar.RefreshClicked += () => {
                if (_vm.RefreshCommand.CanExecute(null))
                    _vm.RefreshCommand.Execute(null);
            };
            toolBar.DeleteClicked += () => {
                if (_vm.EnterSelectModeCommand.CanExecute(null))
                    _vm.EnterSelectModeCommand.Execute(null);
                ApplySelectModeToCards(true);
            };
            toolBar.ConfirmDeleteClicked += () => {
                var ids = CollectSelectedIds();
                if (_vm.ConfirmDeleteCommand.CanExecute(ids))
                    _vm.ConfirmDeleteCommand.Execute(ids);
                ApplySelectModeToCards(false);
            };
            toolBar.CancelSelectClicked += () => {
                if (_vm.CancelSelectCommand.CanExecute(null))
                    _vm.CancelSelectCommand.Execute(null);
                ApplySelectModeToCards(false);
            };
        }

        private void WireEditPanel () {
            if (editPanel == null) return;
            editPanel.CloseRequested += CloseEditPanel;
            editPanel.SaveRequested += () => {
                DeviceInfo info = editPanel.BuildDeviceInfo();
                bool isNew = editPanel.IsNew;
                _vm.SaveDevice(info, isNew);
                CloseEditPanel();
            };
            editPanel.DeleteRequested += () => {
                if (!editPanel.IsNew) {
                    DeviceInfo info = editPanel.BuildDeviceInfo();
                    if (info != null && !string.IsNullOrEmpty(info.Id))
                        _vm.RemoveDevice(info.Id);
                }
                CloseEditPanel();
            };
        }

        // -------------------- 供卡片调用（保持原入口） --------------------

        /// <summary>添加卡点击。</summary>
        public void OpenAddDevice () => _vm.OpenAdd();

        /// <summary>设备卡「编辑」。</summary>
        public void OpenEditDevice (DeviceInfo info) => _vm.OpenEdit(info);

        // -------------------- 弹层 --------------------

        private void ShowEditPanel (bool isNew, DeviceInfo info) {
            if (editPanel == null || editOverlay == null) return;

            if (isNew)
                editPanel.PrepareNew();
            else
                editPanel.Load(info);

            editPanel.Visibility = Visibility.Visible;
            editOverlay.Visibility = Visibility.Visible;
        }

        private void CloseEditPanel () {
            if (editPanel != null)
                editPanel.Visibility = Visibility.Collapsed;
            if (editOverlay != null)
                editOverlay.Visibility = Visibility.Collapsed;
        }

        private void EditOverlay_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            // 点遮罩关闭；点面板内部不要关（面板需 e.Handled）
            CloseEditPanel();
        }

        private void Panel_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            e.Handled = true;
        }

        // -------------------- 多选 --------------------

        private void ApplySelectModeToCards (bool selectMode) {
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList))
                card.SetSelectMode(selectMode);
        }

        private List<string> CollectSelectedIds () {
            var ids = new List<string>();
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList)) {
                if (card.IsSelected && card.Device != null && !string.IsNullOrEmpty(card.Device.Id))
                    ids.Add(card.Device.Id);
            }
            return ids;
        }

        private void DevicePage_Unloaded (object sender, RoutedEventArgs e) {
            // 事件挂在 VM / 工具栏上的匿名委托随 Page 回收即可；
            // 若后续 VM 实现 IDisposable，在此 Dispose。
        }

        private static IEnumerable<T> FindVisualChildren<T> (DependencyObject parent)
            where T : DependencyObject {
            if (parent == null) yield break;
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