using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备新增/编辑面板：以弹窗形式展示表单，负责将 DeviceInfo 加载到控件上，
    /// 及从控件中重新构建出 DeviceInfo。本身不直接操作设备服务，而是通过
    /// CloseRequested/SaveRequested/DeleteRequested 三个事件将意图交给宿主（DevicePage）处理。
    /// </summary>
    public partial class DeviceEditPanel : UserControl {
        /// <summary>请求关闭当前编辑弹窗（不保存）。</summary>
        public event Action CloseRequested;
        /// <summary>请求保存当前表单内容。</summary>
        public event Action SaveRequested;
        /// <summary>请求删除当前正在编辑的设备。</summary>
        public event Action DeleteRequested;

        /// <summary>当前正在编辑的设备 Id；为 null 表示当前处于新增模式。</summary>
        private string _editingId;
        /// <summary>当前选择的轨道模式（单轨/双轨）。</summary>
        private bool _isDual;

        public DeviceEditPanel () {
            InitializeComponent();
        }

        /// <summary>
        /// 加载协议列表到下拉框：从 MyAppServices.Protocols 获取所有协议名称，若没有任何插件则使用默认占位。
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

        /// <summary>
        /// 载入到界面（添加时传新 DeviceInfo 默认值；编辑时传原设备）
        /// </summary>
        /// <param name="info">待加载的设备信息，为 null 时使用默认空对象。</param>
        /// <param name="isNew">是否为新增模式；true 时不记录原有 Id，后续 BuildDeviceInfo 会生成新 Id。</param>
        public void LoadData (DeviceInfo info, bool isNew) {
            if (info == null)
                info = new DeviceInfo();

            if (txtStatus != null) {
                txtStatus.Text = info.StatusText ?? "离线";
                ApplyStatusColor(info.StatusType);
            }
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
            if (txtUnitId != null)
                txtUnitId.Text = info.UnitId.ToString();
            if (txtStatus != null)
                txtStatus.Text = info.StatusText ?? "离线";


            UpdateLaneButtons();

        }

        /// <summary>
        /// 按状态设置状态文字与指示点颜色（与 DeviceCard 一致）。
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
                    key = "SF.Brush.Text.Secondary"; // 离线：灰色
                    break;
            }

            var brush = (System.Windows.Media.Brush)FindResource(key);

            if (txtStatus != null)
                txtStatus.Foreground = brush;

            // 状态圆点
            if (statusDot != null)
                statusDot.Fill = brush;
        }

        /// <summary>
        /// 从界面收集为 DeviceInfo：端口/站号解析失败时使用合理默认值，保证返回对象总是合法可用。
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

        /// <summary>当前面板是否处于新增模式（尚未关联已存在的设备 Id）。</summary>
        public bool IsNew {
            get { return string.IsNullOrEmpty(_editingId); }
        }

        /// <summary>根据协议名称在下拉框中选中对应项，找不到时回退到默认协议或首项。</summary>
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

        /// <summary>获取下拉框当前选中的协议名称文本。</summary>
        private string GetSelectedProtocol () {
            if (cmbProtocol == null || cmbProtocol.SelectedItem == null)
                return "";   // 或 null，保存时再校验

            ComboBoxItem item = cmbProtocol.SelectedItem as ComboBoxItem;
            if (item != null && item.Content != null)
                return item.Content.ToString();

            string s = cmbProtocol.SelectedItem as string;
            return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
        }

        /// <summary>根据当前轨道模式刷新单轨/双轨按钮的高亮样式。</summary>
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

        /// <summary>切换为单轨模式。</summary>
        private void BtnLaneSingle_Click (object sender, RoutedEventArgs e) {
            _isDual = false;
            UpdateLaneButtons();
        }

        /// <summary>切换为双轨模式。</summary>
        private void BtnLaneDual_Click (object sender, RoutedEventArgs e) {
            _isDual = true;
            UpdateLaneButtons();
        }

        /// <summary>关闭按钮点击：转发 CloseRequested 事件。</summary>
        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            if (CloseRequested != null)
                CloseRequested();
        }

        /// <summary>关闭图标（鼠标事件版本）点击：转发 CloseRequested 事件。</summary>
        private void BtnClose_Click (object sender, MouseButtonEventArgs e) {
            if (CloseRequested != null)
                CloseRequested();
        }

        /// <summary>保存按钮点击：转发 SaveRequested 事件。</summary>
        private void BtnSave_Click (object sender, RoutedEventArgs e) {
            if (SaveRequested != null)
                SaveRequested();
        }

        /// <summary>删除按钮点击：转发 DeleteRequested 事件。</summary>
        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            if (DeleteRequested != null)
                DeleteRequested();
        }
    }
}