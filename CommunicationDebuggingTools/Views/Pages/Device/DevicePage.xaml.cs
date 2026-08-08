using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    public partial class DevicePage : Page {
        private readonly ObservableCollection<object> _displayList =
            new ObservableCollection<object>();

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

        private void Devices_CollectionChanged (object sender, NotifyCollectionChangedEventArgs e) {
            RebuildDisplayList();
        }

        private void RebuildDisplayList () {
            _displayList.Clear();

            foreach (DeviceInfo d in MyAppServices.Devices.Devices)
                _displayList.Add(d);

            _displayList.Add(AddDeviceMarker.Instance);

            RefreshCount();
        }

        private void RefreshCount () {
            if (deviceCountText != null)
                deviceCountText.Text = MyAppServices.Devices.Devices.Count.ToString();
        }

        /// <summary>
        /// 添加设备（由 AddDeviceCard 调用）
        /// </summary>
        public void OpenAddDevice () {
            DeviceInfo blank = new DeviceInfo();
            blank.Name = "";
            blank.Model = "";
            blank.Protocol = "Modbus TCP";

            editPanel.LoadData(blank, true);

            editPopup.PlacementTarget = Window.GetWindow(this);
            editPopup.Placement = PlacementMode.Center;
            editPopup.IsOpen = true;
        }

        /// <summary>
        /// 编辑设备（后续 DeviceCard 可调）
        /// </summary>
        public void OpenEditDevice (DeviceInfo info) {
            if (info == null)
                return;

            editPanel.LoadData(info, false);

            editPopup.PlacementTarget = Window.GetWindow(this);
            editPopup.Placement = PlacementMode.Center;
            editPopup.IsOpen = true;
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