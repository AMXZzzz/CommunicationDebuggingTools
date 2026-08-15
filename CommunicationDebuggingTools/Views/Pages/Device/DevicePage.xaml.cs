using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CommunicationDebuggingTools.Views.Pages.Device {

    /// <summary>
    /// 设备管理页：UI 路由；业务在 <see cref="DevicePageViewModel"/>。
    /// DataTemplate 生成的 DeviceCard 无法构造注入，由本页在可视树就绪后赋 DeviceService。
    /// </summary>
    public partial class DevicePage : Page {

        private readonly DevicePageViewModel _vm;

        private readonly DevicePageViewModel _vm;
        private readonly IProtocolResolver _protocols;

        public DevicePage (DevicePageViewModel vm, IProtocolResolver protocols) {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            _vm = vm;
            _protocols = protocols; // 可为 null，编辑面板会显示空协议列表

            InitializeComponent();

            DataContext = _vm;
            deviceList.ItemsSource = _vm.DisplayList;

            WireViewModel();
            WireToolbar();
            WireEditPanel();

            if (toolBar != null)
                toolBar.SetCount(_vm.DeviceCount);

            _vm.DisplayList.CollectionChanged += DisplayList_CollectionChanged;
            Loaded += (_, __) => InjectServicesToCards();

            Unloaded += DevicePage_Unloaded;
        }

        private void WireEditPanel () {
            if (editPanel == null) return;

            // 属性注入：协议下拉不再访问 App.Services
            editPanel.ProtocolResolver = _protocols;

            editPanel.CloseRequested += CloseEditPanel;
            editPanel.SaveRequested += () => {
                DeviceInfo info = editPanel.BuildDeviceInfo();
                _vm.SaveDevice(info, editPanel.IsNew);
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

        private void DisplayList_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) {
            Dispatcher.BeginInvoke(
                new Action(InjectServicesToCards),
                DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 给当前可视树中的所有 DeviceCard 注入 IDeviceService，并同步多选模式。
        /// </summary>
        private void InjectServicesToCards () {
            if (deviceList == null || _vm == null)
                return;

            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList)) {
                card.DeviceService = _vm.Devices;
                card.SetSelectionMode(_vm.IsSelectMode);
            }
        }

        private void WireViewModel () {
            _vm.RequestOpenAdd += () => ShowEditPanel(true, null);
            _vm.RequestOpenEdit += info => ShowEditPanel(false, info);
            _vm.RequestShowError += msg =>
                MessageBox.Show(msg ?? "", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

            _vm.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(DevicePageViewModel.DeviceCount) && toolBar != null)
                    toolBar.SetCount(_vm.DeviceCount);
                if (e.PropertyName == nameof(DevicePageViewModel.IsSelectMode) && toolBar != null) {
                    toolBar.SetSelectMode(_vm.IsSelectMode);
                    ApplySelectModeToCards(_vm.IsSelectMode);
                }
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
                _vm.SaveDevice(info, editPanel.IsNew);
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

        public void OpenAddDevice () => _vm.OpenAdd();

        public void OpenEditDevice (DeviceInfo info) => _vm.OpenEdit(info);

        private void ShowEditPanel (bool isNew, DeviceInfo info) {
            if (editPanel == null || editOverlay == null) return;

            if (isNew)
                editPanel.LoadData(new DeviceInfo(), true);
            else
                editPanel.LoadData(info, false);

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
            CloseEditPanel();
        }

        private void Panel_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            e.Handled = true;
        }

        private void ApplySelectModeToCards (bool selectMode) {
            foreach (DeviceCard card in FindVisualChildren<DeviceCard>(deviceList)) {
                card.DeviceService = _vm.Devices; // 确保已注入
                card.SetSelectionMode(selectMode);
            }
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
            _vm.DisplayList.CollectionChanged -= DisplayList_CollectionChanged;
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