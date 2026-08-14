using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Core.Tools;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备新增 / 编辑面板（弹窗内容）。
    /// <para>
    /// 职责：在控件与 <see cref="DeviceInfo"/> 之间做加载、收集；
    /// 不直接调用设备服务，通过事件把关闭 / 保存 / 删除交给 DevicePage。
    /// </para>
    /// <para>
    /// 协议私有连接参数（如 Modbus 站号）只写入
    /// <see cref="DeviceInfo.ProtocolSettingsJson"/>，不再使用已删除的 UnitId 属性。
    /// </para>
    /// </summary>
    public partial class DeviceEditPanel : UserControl {
        /// <summary>请求关闭弹窗（不保存）。</summary>
        public event Action CloseRequested;
        /// <summary>请求保存当前表单。</summary>
        public event Action SaveRequested;
        /// <summary>请求删除当前正在编辑的设备。</summary>
        public event Action DeleteRequested;

        /// <summary>编辑中的设备 Id；为 null 表示新增模式。</summary>
        private string _editingId;
        /// <summary>当前是否选择双轨。</summary>
        private bool _isDual;

        public DeviceEditPanel () {
            InitializeComponent();
        }

        /// <summary>是否处于新增（尚未绑定已有设备 Id）。</summary>
        public bool IsNew => string.IsNullOrEmpty(_editingId);

        /// <summary>
        /// 从 MyAppServices.Protocols 填充协议下拉。
        /// 无插件时列表为空，由保存逻辑拒绝空协议。
        /// </summary>
        private void LoadProtocolList () {
            if (cmbProtocol == null)
                return;

            cmbProtocol.Items.Clear();
            if (MyAppServices.Protocols == null)
                return;

            IList<string> names = MyAppServices.Protocols.GetProtocolNames();
            if (names == null)
                return;

            foreach (string name in names) {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                cmbProtocol.Items.Add(new ComboBoxItem { Content = name });
            }

            if (cmbProtocol.Items.Count > 0)
                cmbProtocol.SelectedIndex = 0;
        }

        /// <summary>将 info 载入界面控件。</summary>
        /// <param name="info">设备数据；null 时按默认空设备处理。</param>
        /// <param name="isNew">true 为新增（不记录 Id）。</param>
        public void LoadData (DeviceInfo info, bool isNew) {
            if (info == null)
                info = new DeviceInfo();

            _editingId = isNew ? null : info.Id;
            _isDual = info.IsDualLane;

            LoadProtocolList();
            SelectProtocol(info.Protocol);

            if (txtName != null)
                txtName.Text = info.Name ?? "";
            if (txtModel != null)
                txtModel.Text = info.Model ?? "";
            if (txtIp != null)
                txtIp.Text = info.Ip ?? "";
            if (txtPort != null)
                txtPort.Text = info.Port.ToString();

            // 站号仅存在于 ProtocolSettingsJson
            if (txtUnitId != null)
                txtUni  tId.Text = ProtocolSettingsJson.GetInt(info.ProtocolSettingsJson, "unitId", 1).ToString();

            if (txtStatus != null) {
                txtStatus.Text = info.StatusText ?? "离线";
                ApplyStatusColor(info.StatusType);
            }

            UpdateLaneButtons();
        }

        /// <summary>
        /// 从界面收集为 DeviceInfo。
        /// 端口解析失败时用 502；站号写入 ProtocolSettingsJson。
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
                ? port
                : 502;

            d.Protocol = GetSelectedProtocol();
            d.IsDualLane = _isDual;
            d.ProtocolSettingsJson = BuildUnitIdSettingsJson();

            // 新增默认离线；编辑不在此处改连接状态
            if (string.IsNullOrEmpty(_editingId)) {
                d.IsConnected = false;
                d.StatusType = DeviceStatusType.Offline;
            }

            return d;
        }

        /// <summary>
        /// 根据站号输入框生成 ProtocolSettingsJson。
        /// 后续若有动态表单，可改为序列化完整字段集。
        /// </summary>
        private string BuildUnitIdSettingsJson () {
            int unit = 1;
            if (txtUnitId != null) {
                int.TryParse(txtUnitId.Text.Trim(), out unit);
                if (unit < 0) unit = 0;
                if (unit > 99) unit = 99;
            }
            // unitId：Modbus；station：松下（同一输入框）
            return "{\"unitId\":" + unit + ",\"station\":" + unit + "}";
        }

        /// <summary>
        /// 按 DeviceStatusType 设置状态文字与圆点颜色（与设备卡一致）。
        /// </summary>
        private void ApplyStatusColor (DeviceStatusType type) {
            string key;
            switch (type) {
                case DeviceStatusType.Success:
                    key = "SF.Brush.Status.Success";
                    break;
                case DeviceStatusType.Connecting:
                case DeviceStatusType.Warning:
                    key = "SF.Brush.Status.Warning";
                    break;
                case DeviceStatusType.Error:
                    key = "SF.Brush.Status.Error";
                    break;
                default:
                    key = "SF.Brush.Text.Secondary";
                    break;
            }

            var brush = (System.Windows.Media.Brush)FindResource(key);
            if (txtStatus != null)
                txtStatus.Foreground = brush;
            if (statusDot != null)
                statusDot.Fill = brush;
        }

        /// <summary>在协议下拉中选中指定名称；找不到则选第一项。</summary>
        private void SelectProtocol (string protocol) {
            if (cmbProtocol == null)
                return;

            if (string.IsNullOrEmpty(protocol))
                protocol = "Modbus TCP";

            for (int i = 0; i < cmbProtocol.Items.Count; i++) {
                ComboBoxItem item = cmbProtocol.Items[i] as ComboBoxItem;
                string text = item != null && item.Content != null
                    ? item.Content.ToString()
                    : (cmbProtocol.Items[i] != null ? cmbProtocol.Items[i].ToString() : "");

                if (string.Equals(text, protocol, StringComparison.OrdinalIgnoreCase)) {
                    cmbProtocol.SelectedIndex = i;
                    return;
                }
            }

            if (cmbProtocol.Items.Count > 0)
                cmbProtocol.SelectedIndex = 0;
        }

        /// <summary>读取当前选中的协议显示名。</summary>
        private string GetSelectedProtocol () {
            if (cmbProtocol == null || cmbProtocol.SelectedItem == null)
                return "";

            ComboBoxItem item = cmbProtocol.SelectedItem as ComboBoxItem;
            if (item != null && item.Content != null)
                return item.Content.ToString();

            string s = cmbProtocol.SelectedItem as string;
            return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
        }

        /// <summary>根据 _isDual 刷新单轨 / 双轨按钮样式。</summary>
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

        /// <summary>设置轨道模式并刷新按钮样式。</summary>
        private void SetLane (bool dual) {
            _isDual = dual;
            UpdateLaneButtons();
        }

        private void BtnLaneSingle_Click (object sender, RoutedEventArgs e) => SetLane(false);
        private void BtnLaneDual_Click (object sender, RoutedEventArgs e) => SetLane(true);
        private void BtnClose_Click (object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
        private void BtnClose_Click (object sender, MouseButtonEventArgs e) => CloseRequested?.Invoke();
        private void BtnSave_Click (object sender, RoutedEventArgs e) => SaveRequested?.Invoke();
        private void BtnDelete_Click (object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    }
}