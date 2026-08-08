using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    public partial class DeviceEditPanel : UserControl {
        public event Action CloseRequested;
        public event Action SaveRequested;
        public event Action DeleteRequested;

        private bool _isDual = true;

        public DeviceEditPanel () {
            InitializeComponent();
            UpdateLaneButtons();
        }

        /// <summary>
        /// 载入编辑数据
        /// </summary>
        public void LoadData (string name, string model, string protocol,
                             string ip, string port, string unitId,
                             bool isDual, string statusText) {
            txtName.Text = name;
            txtModel.Text = model;
            txtIp.Text = ip;
            txtPort.Text = port;
            txtUnitId.Text = unitId;
            _isDual = isDual;
            UpdateLaneButtons();

            if (txtStatus != null)
                txtStatus.Text = statusText;

            // 协议下拉匹配
            if (cmbProtocol != null) {
                for (int i = 0; i < cmbProtocol.Items.Count; i++) {
                    if (cmbProtocol.Items[i] is ComboBoxItem item &&
                        string.Equals(item.Content?.ToString(), protocol, StringComparison.OrdinalIgnoreCase)) {
                        cmbProtocol.SelectedIndex = i;
                        break;
                    }
                }
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

        private void UpdateLaneButtons () {
            if (btnLaneSingle == null || btnLaneDual == null) return;

            if (_isDual) {
                btnLaneDual.Style = (Style)FindResource("SF.Style.PrimaryButton");
                btnLaneSingle.Style = (Style)FindResource("SF.Style.DarkButton");
            } else {
                btnLaneSingle.Style = (Style)FindResource("SF.Style.PrimaryButton");
                btnLaneDual.Style = (Style)FindResource("SF.Style.DarkButton");
            }
        }

        // 关闭按钮 / 取消
        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            CloseRequested?.Invoke();
        }

        // 标题栏 ×（MouseLeftButtonUp）
        private void BtnClose_Click (object sender, MouseButtonEventArgs e) {
            CloseRequested?.Invoke();
        }

        private void BtnSave_Click (object sender, RoutedEventArgs e) {
            SaveRequested?.Invoke();
        }

        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            DeleteRequested?.Invoke();
        }
    }
}