using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Views.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace CommunicationDebuggingTools.Views.VariableConfigPage {

    /// <summary>
    /// 变量配置页：组装 Controls；用遮罩 + Visibility 调度
    /// 编辑 / 批量 / 导入 / 导出 / 主题消息框。
    /// </summary>
    public partial class VariableConfigPage : Page {

        private enum MsgPending {
            None,
            ImportClear
        }

        private string _selectedDeviceId;
        private string _lastExportPath;
        private MsgPending _msgPending;

        /// <summary>从 DI 取服务（过渡写法；后续可改为构造注入 ViewModel）。</summary>
        private static T Svc<T> () where T : class =>
            App.Services != null ? App.Services.GetService<T>() : null;

        public VariableConfigPage () {
            InitializeComponent();
            WireEvents();
            deviceList.Reload();
        }

        private void WireEvents () {
            deviceList.DeviceSelected += OnDeviceSelected;
            variableTable.VariablesChanged += () => deviceList.Reload();
            variableTable.EditRequested += OpenEdit;
            variableTable.WriteRequested += OnVariableWriteRequested;

            deviceHeader.AddClicked += OpenAdd;
            deviceHeader.BatchAddClicked += OpenBatch;

            if (toolBar != null) {
                toolBar.ImportClicked += OpenImport;
                toolBar.ExportClicked += OpenExport;
            }

            if (editPanel != null) {
                editPanel.CloseRequested += CloseEdit;
                editPanel.SaveRequested += SaveEdit;
                editPanel.DeleteRequested += DeleteEdit;
            }

            if (batchPanel != null) {
                batchPanel.CloseRequested += CloseBatch;
                batchPanel.BatchSaveRequested += SaveBatch;
            }

            if (exportPanel != null) {
                exportPanel.CloseRequested -= CloseExport;
                exportPanel.ExportSucceeded -= OnExportSucceeded;
                exportPanel.InfoRequested -= OnPanelInfo;

                exportPanel.CloseRequested += CloseExport;
                exportPanel.ExportSucceeded += OnExportSucceeded;
                exportPanel.InfoRequested += OnPanelInfo;
            }

            if (importPanel != null) {
                importPanel.CloseRequested -= CloseImport;
                importPanel.ConfirmClearRequested -= OnImportConfirmClear;
                importPanel.ImportSucceeded -= OnImportSucceeded;
                importPanel.InfoRequested -= OnPanelInfo;

                importPanel.CloseRequested += CloseImport;
                importPanel.ConfirmClearRequested += OnImportConfirmClear;
                importPanel.ImportSucceeded += OnImportSucceeded;
                importPanel.InfoRequested += OnPanelInfo;
            }

            if (msgDialog != null) {
                msgDialog.CloseRequested -= OnMsgClose;
                msgDialog.PrimaryRequested -= OnMsgPrimary;
                msgDialog.SecondaryRequested -= OnMessageSecondary;

                msgDialog.CloseRequested += OnMsgClose;
                msgDialog.PrimaryRequested += OnMsgPrimary;
                msgDialog.SecondaryRequested += OnMessageSecondary;
            }
        }

        private void OnDeviceSelected (string deviceId) {
            _selectedDeviceId = deviceId;
            deviceHeader.Show(deviceId);
            variableTable.Load(deviceId);
        }

        // -------------------- 单条 --------------------

        private void OpenAdd () {
            if (!EnsureDeviceSelected() || editPanel == null) return;
            editPanel.PrepareNew();
            ShowPanel(editPanel);
        }

        public void OpenEdit (VariableItem item) {
            if (item == null || editPanel == null) return;
            _selectedDeviceId = item.DeviceId;
            editPanel.Load(item);
            ShowPanel(editPanel);
        }

        private void CloseEdit () => HidePanel(editPanel);

        private void SaveEdit () {
            IVariableService vars = Svc<IVariableService>();
            if (editPanel == null || vars == null) return;

            VariableItem built = editPanel.Build();
            if (built == null) return;

            if (string.IsNullOrEmpty(built.DeviceId))
                built.DeviceId = _selectedDeviceId;

            if (editPanel.IsNew)
                vars.Add(built);
            else
                vars.Update(built);

            CloseEdit();
            RefreshList();
        }

        private void DeleteEdit () {
            if (editPanel == null || editPanel.IsNew || string.IsNullOrEmpty(editPanel.EditingId))
                return;

            IVariableService vars = Svc<IVariableService>();
            if (vars == null) return;

            vars.Remove(editPanel.EditingId);
            CloseEdit();
            RefreshList();
        }

        // -------------------- 批量 --------------------

        private void OpenBatch () {
            if (!EnsureDeviceSelected() || batchPanel == null) return;
            batchPanel.Prepare(GetSelectedDeviceTitle());
            ShowPanel(batchPanel);
        }

        private void CloseBatch () => HidePanel(batchPanel);

        private void SaveBatch (IList<VariableItem> items) {
            IVariableService vars = Svc<IVariableService>();
            if (items == null || vars == null || string.IsNullOrEmpty(_selectedDeviceId))
                return;

            foreach (VariableItem v in items) {
                if (v == null) continue;
                v.DeviceId = _selectedDeviceId;
                vars.Add(v);
            }

            CloseBatch();
            RefreshList();
        }

        // -------------------- 导出 --------------------

        private void OpenExport () {
            if (exportPanel == null) return;
            exportPanel.Prepare(_selectedDeviceId, GetSelectedDeviceTitle(), CountCurrentVariables());
            ShowPanel(exportPanel);
        }

        private void CloseExport () => HidePanel(exportPanel);

        private void OnExportSucceeded (string path, int count) {
            CloseExport();
            ShowExportSuccess(path, count);
        }

        private void ShowExportSuccess (string path, int count) {
            if (msgDialog == null) return;
            _lastExportPath = path;
            _msgPending = MsgPending.None;
            msgDialog.Setup(
                AppMessageKind.Success,
                "导出完成",
                "已导出 " + count + " 条变量",
                path,
                primaryText: "确定",
                secondaryText: "打开目录",
                showSecondary: true,
                detailAsBox: true);
            ShowPanel(msgDialog);
        }

        // -------------------- 导入 --------------------

        private void OpenImport () {
            if (importPanel == null) return;
            importPanel.Prepare(_selectedDeviceId, GetSelectedDeviceTitle());
            ShowPanel(importPanel);
        }

        private void CloseImport () => HidePanel(importPanel);

        private void OnImportConfirmClear (string title, string detail) {
            if (msgDialog == null) return;
            _msgPending = MsgPending.ImportClear;
            msgDialog.Setup(
                AppMessageKind.Warning,
                title,
                "导入将覆盖当前范围内变量",
                detail,
                primaryText: "继续导入",
                secondaryText: "取消",
                showSecondary: true);
            ShowPanel(msgDialog);
        }

        private void OnImportSucceeded (int count) {
            RefreshList();
            if (msgDialog == null) return;
            _msgPending = MsgPending.None;
            msgDialog.Setup(
                AppMessageKind.Success,
                "导入完成",
                "已导入 " + count + " 条变量",
                detail: null,
                primaryText: "确定",
                showSecondary: false);
            ShowPanel(msgDialog);
        }

        // -------------------- 主题消息框 --------------------

        private void OnMsgPrimary () {
            MsgPending pending = _msgPending;
            _msgPending = MsgPending.None;
            HidePanel(msgDialog);
            if (pending == MsgPending.ImportClear)
                importPanel?.ExecuteImport();
        }

        private void OnMsgClose () {
            _msgPending = MsgPending.None;
            HidePanel(msgDialog);
        }

        private void OnMessageSecondary () {
            if (string.IsNullOrEmpty(_lastExportPath))
                return;

            string dir = Path.GetDirectoryName(_lastExportPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
                ShowInfo("提示", "目录不存在或已被删除");
                return;
            }

            try {
                Process.Start(new ProcessStartInfo {
                    FileName = dir,
                    UseShellExecute = true
                });
            } catch (InvalidOperationException ex) {
                ShowInfo("打开目录失败", ex.Message);
            } catch (System.ComponentModel.Win32Exception ex) {
                ShowInfo("打开目录失败", ex.Message);
            }
        }

        private async void OnVariableWriteRequested (string variableId, string writeText) {
            IVariableService vars = Svc<IVariableService>();
            if (string.IsNullOrWhiteSpace(variableId) || vars == null)
                return;

            VariableItem variable = vars.Variables
                .FirstOrDefault(v => v != null && v.Id == variableId);
            if (variable == null) {
                ShowInfo("写入失败", "变量不存在");
                return;
            }

            if (!TryParseWriteValue(variable.DataType, writeText, out object value, out string parseError)) {
                ShowInfo("写入失败", parseError);
                return;
            }

            bool ok;
            try {
                ok = await vars.WriteAsync(variableId, value, CancellationToken.None);
            } catch (ArgumentException ex) {
                ShowInfo("写入失败", ex.Message);
                return;
            } catch (InvalidOperationException ex) {
                ShowInfo("写入失败", ex.Message);
                return;
            }

            if (!ok) {
                string error = string.IsNullOrWhiteSpace(variable.LastError)
                    ? "写入未成功"
                    : variable.LastError;
                ShowInfo("写入失败", error);
            }

            RefreshList();
        }

        private static bool TryParseWriteValue (
            Core.Enums.VariableDataType dataType,
            string raw,
            out object value,
            out string error) {
            string text = (raw ?? string.Empty).Trim();
            value = null;
            error = null;

            switch (dataType) {
                case Core.Enums.VariableDataType.Bool:
                    if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                        value = true;
                        return true;
                    }
                    if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) {
                        value = false;
                        return true;
                    }
                    error = "Bool 类型仅支持 true/false 或 1/0";
                    return false;

                case Core.Enums.VariableDataType.Int16:
                    if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short i16)) {
                        value = i16;
                        return true;
                    }
                    error = "Int16 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.UInt16:
                    if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u16)) {
                        value = u16;
                        return true;
                    }
                    error = "UInt16 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.Int32:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32)) {
                        value = i32;
                        return true;
                    }
                    error = "Int32 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.UInt32:
                    if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u32)) {
                        value = u32;
                        return true;
                    }
                    error = "UInt32 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.Int64:
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64)) {
                        value = i64;
                        return true;
                    }
                    error = "Int64 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.UInt64:
                    if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u64)) {
                        value = u64;
                        return true;
                    }
                    error = "UInt64 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.Float:
                    if (float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out float f)) {
                        value = f;
                        return true;
                    }
                    error = "Float 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.Double:
                    if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out double d)) {
                        value = d;
                        return true;
                    }
                    error = "Double 类型格式不正确";
                    return false;

                case Core.Enums.VariableDataType.String:
                    value = text;
                    return true;

                default:
                    value = text;
                    return true;
            }
        }

        private void OnPanelInfo (string title, string message) =>
            ShowInfo(title, message);

        private void ShowInfo (string title, string message) {
            if (msgDialog == null) return;
            _msgPending = MsgPending.None;
            msgDialog.Setup(
                AppMessageKind.Info,
                title,
                message,
                detail: null,
                primaryText: "确定",
                secondaryText: null,
                showSecondary: false);
            ShowPanel(msgDialog);
        }

        // -------------------- 遮罩 --------------------

        private void EditOverlay_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) =>
            CloseAllPanels();

        private void ShowPanel (UIElement panel) {
            HideAllPanels();
            if (panel != null)
                panel.Visibility = Visibility.Visible;
            if (editOverlay != null)
                editOverlay.Visibility = Visibility.Visible;
        }

        private void HidePanel (UIElement panel) {
            if (panel != null)
                panel.Visibility = Visibility.Collapsed;
            HideOverlayIfIdle();
        }

        private void HideAllPanels () {
            SetCollapsed(editPanel);
            SetCollapsed(batchPanel);
            SetCollapsed(exportPanel);
            SetCollapsed(importPanel);
            SetCollapsed(msgDialog);
        }

        private void CloseAllPanels () {
            _msgPending = MsgPending.None;
            HideAllPanels();
            if (editOverlay != null)
                editOverlay.Visibility = Visibility.Collapsed;
        }

        private void HideOverlayIfIdle () {
            bool busy =
                IsShown(editPanel) || IsShown(batchPanel) ||
                IsShown(exportPanel) || IsShown(importPanel) ||
                IsShown(msgDialog);

            if (!busy && editOverlay != null)
                editOverlay.Visibility = Visibility.Collapsed;
        }

        private static void SetCollapsed (UIElement e) {
            if (e != null)
                e.Visibility = Visibility.Collapsed;
        }

        private static bool IsShown (UIElement e) =>
            e != null && e.Visibility == Visibility.Visible;

        // -------------------- 工具 --------------------

        private void RefreshList () {
            variableTable.Load(_selectedDeviceId);
            deviceList.Reload();
        }

        private int CountCurrentVariables () {
            IVariableService vars = Svc<IVariableService>();
            if (vars == null || string.IsNullOrEmpty(_selectedDeviceId))
                return 0;
            return vars.Variables
                .Count(v => v != null && v.DeviceId == _selectedDeviceId);
        }

        private bool EnsureDeviceSelected () {
            if (!string.IsNullOrEmpty(_selectedDeviceId))
                return true;
            ShowInfo("提示", "请先选择左侧设备");
            return false;
        }

        private string GetSelectedDeviceTitle () {
            IDeviceService devices = Svc<IDeviceService>();
            if (devices == null || string.IsNullOrEmpty(_selectedDeviceId))
                return "";

            DeviceInfo d = devices.Devices
                .FirstOrDefault(x => x != null && x.Id == _selectedDeviceId);
            if (d == null) return "";

            string name = string.IsNullOrEmpty(d.Name) ? d.Id : d.Name;
            return string.IsNullOrEmpty(d.Model) ? name : (name + " · " + d.Model);
        }
    }
}