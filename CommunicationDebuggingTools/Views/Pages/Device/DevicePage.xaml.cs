using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备管理页面：展示设备卡片列表（末尾附带一个“添加设备”占位卡片），
    /// 并通过弹出式编辑面板（editPanel/editPopup）完成设备的新增/编辑/删除。
    /// </summary>
    public partial class DevicePage : Page {
        /// <summary>
        /// 实际绑定到 ItemsControl 的显示列表，内容为设备列表 + 末尾的 <see cref="AddDeviceMarker"/> 占位项。
        /// 与 MyAppServices.Devices.Devices 解耦，避免 XAML 直接绑定业务集合导致模板匹配复杂化。
        /// </summary>
        private readonly ObservableCollection<object> _displayList =
            new ObservableCollection<object>();

        /// <summary>
        /// 构造页面：初始化控件、构建显示列表，并订阅设备集合变化事件以保持列表实时同步，
        /// 同时订阅编辑面板的关闭/保存/删除请求事件。
        /// </summary>
        public DevicePage () {
            InitializeComponent();

            deviceList.ItemsSource = _displayList;
            RebuildDisplayList();

            MyAppServices.Devices.Devices.CollectionChanged += Devices_CollectionChanged;

            if (editPanel != null) {
                editPanel.CloseRequested += () => { editPopup.IsOpen = false; };
                editPanel.SaveRequested += EditPanel_SaveRequested;
                editPanel.DeleteRequested += EditPanel_DeleteRequested;
            }
        }

        /// <summary>设备集合发生增删改时重建显示列表。</summary>
        private void Devices_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) {
            RebuildDisplayList();
        }

        /// <summary>
        /// 重建显示列表：清空后依次加入全部设备，最后追加 <see cref="AddDeviceMarker"/> 占位项，
        /// 保证“添加设备”卡片始终显示在列表末尾。
        /// </summary>
        private void RebuildDisplayList () {
            _displayList.Clear();

            foreach (DeviceInfo d in MyAppServices.Devices.Devices)
                _displayList.Add(d);

            _displayList.Add(AddDeviceMarker.Instance);

            RefreshCount();
        }

        /// <summary>刷新页面上显示的设备数量文本。</summary>
        private void RefreshCount () {
            if (deviceCountText != null)
                deviceCountText.Text = MyAppServices.Devices.Devices.Count.ToString();
        }

        /// <summary>
        /// 添加设备（由 AddDeviceCard 调用）
        /// </summary>
        public void OpenAddDevice () {
            DeviceInfo blank = new DeviceInfo {
                Name = "",
                Model = "",
                Protocol = "Modbus TCP"
            };

            editPanel.LoadData(blank, true);
            ShowEditPopup();
        }

        /// <summary>
        /// 编辑设备（后续 DeviceCard 可调）
        /// </summary>
        public void OpenEditDevice (DeviceInfo info) {
            if (info == null)
                return;

            editPanel.LoadData(info, false);
            ShowEditPopup();
        }

        /// <summary>
        /// 延后打开 Popup，避免 MouseUp 导致 StaysOpen=false 的弹窗立刻关闭。
        /// </summary>
        private void ShowEditPopup () {
            Window owner = Window.GetWindow(this);
            if (owner != null)
                editPopup.PlacementTarget = owner;

            editPopup.Placement = PlacementMode.Center;

            // 若当前已在鼠标事件中打开，同一次抬起会被 Popup 当成“外部点击”而马上关掉
            Dispatcher.BeginInvoke(new Action(() => {
                editPopup.IsOpen = true;
            }), DispatcherPriority.Input);
        }

        private void EditPanel_SaveRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();

            if (string.IsNullOrWhiteSpace(info.Name)) {
                MessageBox.Show("请填写设备名称", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (editPanel.IsNew)
                MyAppServices.Devices.Add(info);
            else
                MyAppServices.Devices.Update(info);

            editPopup.IsOpen = false;
        }

        /// <summary>
        /// 删除确认请求：弹出二次确认对话框，用户确认后才会真正调用 Remove 并关闭弹窗。
        /// </summary>
        private void EditPanel_DeleteRequested () {
            DeviceInfo info = editPanel.BuildDeviceInfo();
            if (editPanel.IsNew || string.IsNullOrEmpty(info.Id)) {
                editPopup.IsOpen = false;
                return;
            }

            MessageBoxResult r = MessageBox.Show(
                "确定删除该设备？",
                "确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes) {
                MyAppServices.Devices.Remove(info.Id);
                editPopup.IsOpen = false;
            }
        }
    }
}