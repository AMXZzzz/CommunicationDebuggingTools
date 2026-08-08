using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    public partial class DeviceEditPanel : UserControl {
        public event Action CloseRequested;
        public event Action SaveRequested;
        public event Action DeleteRequested;

        private string _editingId;
        private bool _isDual;

        public DeviceEditPanel () {
            InitializeComponent();
        }

        /// <summary>
        /// 载入到界面（添加时传新 DeviceInfo 默认值；编辑时传原设备）
        /// </summary>
        public void LoadData (DeviceInfo info, bool isNew) {
            if (info == null)
                info = new DeviceInfo();

            _editingId = isNew ? null : info.Id;
            _isDual = info.IsDualLane;

            if (txtName != null)
                txtName.Text = info.Name ?? "";
            if (txtModel != null)
                txtModel.Text = info.Model ?? "";
            if (txtIp != null)
                txtIp.Text = info.Ip ?? "";
            if (txtPort != null)
                txtPort.Text = info.Port.ToString();
            if (txtUnitId != null)
                txtUnitId.Text = info.UnitId.ToString();
            if (txtStatus != null)
                txtStatus.Text = info.StatusText ?? "离线";

            SelectProtocol(info.Protocol);
            UpdateLaneButtons();

            // 新建时隐藏删除
            //if (btnDelete != null)
            //    btnDelete.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 从界面收集为 DeviceInfo
        /// </summary>
        public DeviceInfo BuildDeviceInfo () {
            DeviceInfo d = new DeviceInfo();

            if (!string.IsNullOrEmpty(_editingId))
                d.Id = _editingId;

            d.Name = txtName != null ? txtName.Text.Trim() : "";
            d.Model = txtModel != null ? txtModel.Text.Trim() : "";
            d.Ip = txtIp != null ? txtIp.Text.Trim() : "";

            int port;
            d.Port = (txtPort != null && int.TryParse(txtPort.Text.Trim(), out port))
                ? port : 502;

            int unit;
            d.UnitId = (txtUnitId != null && int.TryParse(txtUnitId.Text.Trim(), out unit))
                ? unit : 1;

            d.Protocol = GetSelectedProtocol();
            d.IsDualLane = _isDual;

            // 编辑时不在这里改连接状态；新建默认离线
            if (string.IsNullOrEmpty(_editingId)) {
                d.IsConnected = false;
                d.StatusType = DeviceStatusType.Offline;
            }

            return d;
        }

        public bool IsNew {
            get { return string.IsNullOrEmpty(_editingId); }
        }

        private void SelectProtocol (string protocol) {
            if (cmbProtocol == null)
                return;

            if (string.IsNullOrEmpty(protocol))
                protocol = "Modbus TCP";

            for (int i = 0; i < cmbProtocol.Items.Count; i++) {
                ComboBoxItem item = cmbProtocol.Items[i] as ComboBoxItem;
                string text = item != null
                    ? (item.Content != null ? item.Content.ToString() : "")
                    : (cmbProtocol.Items[i] != null ? cmbProtocol.Items[i].ToString() : "");

                if (string.Equals(text, protocol, StringComparison.OrdinalIgnoreCase)) {
                    cmbProtocol.SelectedIndex = i;
                    return;
                }
            }

            if (cmbProtocol.Items.Count > 0)
                cmbProtocol.SelectedIndex = 0;
        }

        private string GetSelectedProtocol () {
            if (cmbProtocol == null || cmbProtocol.SelectedItem == null)
                return "Modbus TCP";

            ComboBoxItem item = cmbProtocol.SelectedItem as ComboBoxItem;
            if (item != null && item.Content != null)
                return item.Content.ToString();

            return cmbProtocol.SelectedItem.ToString();
        }

        private void UpdateLaneButtons () {
            if (btnLaneSingle == null || btnLaneDual == null)
                return;

            if (_isDual) {
                btnLaneDual.Style = (Style)FindResource("SF.Style.PrimaryButton");
                btnLaneSingle.Style = (Style)FindResource("SF.Style.DarkButton");
            } else {
                btnLaneSingle.Style = (Style)FindResource("SF.Style.PrimaryButton");
                btnLaneDual.Style = (Style)FindResource("SF.Style.DarkButton");
            }
        }

        private void BtnLaneSingle_Click (object sender, RoutedEventArgs e) {
            _isDual = false;
            UpdateLaneButtons();
        }

        private void BtnLaneDual_Click (object sender, RoutedEventArgs e) {
            _isDual = true;
            UpdateLaneButtons();
        }

        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            if (CloseRequested != null)
                CloseRequested();
        }

        private void BtnClose_Click (object sender, MouseButtonEventArgs e) {
            if (CloseRequested != null)
                CloseRequested();
        }

        private void BtnSave_Click (object sender, RoutedEventArgs e) {
            if (SaveRequested != null)
                SaveRequested();
        }

        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            if (DeleteRequested != null)
                DeleteRequested();
        }
    }
}