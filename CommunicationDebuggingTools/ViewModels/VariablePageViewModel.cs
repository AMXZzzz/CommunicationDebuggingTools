using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 变量配置页 ViewModel。
    /// 持有选中设备、编辑状态；Page 只负责面板显示/隐藏。
    /// </summary>
    public sealed class VariablePageViewModel : ViewModelBase {

        private readonly IVariableService _variables;
        private readonly IDeviceService   _devices;
        private readonly IAppLogger       _log;

        // ── 状态 ────────────────────────────────────
        private string _selectedDeviceId;
        public string SelectedDeviceId {
            get => _selectedDeviceId;
            set {
                if (SetField(ref _selectedDeviceId, value)) {
                    OnPropertyChanged(nameof(HasDeviceSelected));
                    OnPropertyChanged(nameof(SelectedDeviceTitle));
                }
            }
        }

        public bool HasDeviceSelected => !string.IsNullOrEmpty(_selectedDeviceId);

        public string SelectedDeviceTitle {
            get {
                if (!HasDeviceSelected) return string.Empty;
                DeviceInfo d = _devices.Devices
                    .FirstOrDefault(x => x != null && x.Id == _selectedDeviceId);
                if (d == null) return string.Empty;
                string name = string.IsNullOrEmpty(d.Name) ? d.Id : d.Name;
                return string.IsNullOrEmpty(d.Model) ? name : (name + " · " + d.Model);
            }
        }

        public int CurrentVariableCount =>
            HasDeviceSelected
            ? _variables.Variables.Count(v => v != null && v.DeviceId == _selectedDeviceId)
            : 0;

        // ── 事件（View 订阅，处理面板显示/隐藏）────────
        public event Action             RequestOpenAdd;
        public event Action<VariableItem> RequestOpenEdit;
        public event Action             RequestOpenBatch;
        public event Action             RequestOpenImport;
        public event Action<string, int>  RequestOpenExport;   // deviceId, count
        public event Action<string, string> RequestShowInfo;   // title, message
        public event Action<string, string, string> RequestShowWarning; // title, msg, detail

        // ── 构造 ────────────────────────────────────
        public VariablePageViewModel (
            IVariableService variables,
            IDeviceService devices,
            IAppLogger logger = null) {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _log = logger;
        }

        // ── 设备选择 ─────────────────────────────────
        public void SelectDevice (string deviceId) {
            SelectedDeviceId = deviceId;
        }

        // ── 变量 CRUD ────────────────────────────────
        public void OpenAdd () {
            if (!EnsureDevice()) return;
            RequestOpenAdd?.Invoke();
        }

        public void OpenEdit (VariableItem item) {
            if (item == null) return;
            SelectedDeviceId = item.DeviceId;
            RequestOpenEdit?.Invoke(item);
        }

        public void SaveVariable (VariableItem item, bool isNew) {
            if (item == null) return;
            if (string.IsNullOrEmpty(item.DeviceId))
                item.DeviceId = _selectedDeviceId;
            try {
                if (isNew) _variables.Add(item);
                else _variables.Update(item);
                _log?.Info("Variable", (isNew ? "新增" : "更新") + "变量: " + item.Name);
            } catch (Exception ex) {
                RequestShowInfo?.Invoke("保存失败", ex.Message);
                _log?.Error("Variable", "保存变量失败", ex);
            }
        }

        public void DeleteVariable (string id) {
            if (string.IsNullOrEmpty(id)) return;
            try {
                _variables.Remove(id);
                _log?.Info("Variable", "删除变量: " + id);
            } catch (Exception ex) {
                RequestShowInfo?.Invoke("删除失败", ex.Message);
                _log?.Error("Variable", "删除变量失败", ex);
            }
        }

        // ── 批量新增 ─────────────────────────────────
        public void OpenBatch () {
            if (!EnsureDevice()) return;
            RequestOpenBatch?.Invoke();
        }

        public void SaveBatch (IList<VariableItem> items) {
            if (items == null || string.IsNullOrEmpty(_selectedDeviceId)) return;
            foreach (VariableItem v in items) {
                if (v == null) continue;
                v.DeviceId = _selectedDeviceId;
                try { _variables.Add(v); } catch { }
            }
            _log?.Info("Variable", "批量新增 " + items.Count + " 条变量");
        }

        // ── 导入 / 导出 ──────────────────────────────
        public void OpenImport () {
            if (!EnsureDevice()) return;
            RequestOpenImport?.Invoke();
        }

        public void OpenExport () {
            RequestOpenExport?.Invoke(_selectedDeviceId, CurrentVariableCount);
        }

        public void NotifyImportConfirmClear (string title, string detail) {
            RequestShowWarning?.Invoke(title, "导入将覆盖当前范围内变量", detail);
        }

        public void NotifyImportSucceeded (int count) {
            RequestShowInfo?.Invoke("导入完成", "已导入 " + count + " 条变量");
            _log?.Info("Variable", "导入完成，共 " + count + " 条");
        }

        public void NotifyExportSucceeded (string path, int count) {
            _log?.Info("Variable", "导出完成，共 " + count + " 条，路径: " + path);
        }

        public void OpenExportFolder (string path) {
            if (string.IsNullOrEmpty(path)) return;
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
                RequestShowInfo?.Invoke("提示", "目录不存在或已被删除");
                return;
            }
            try {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            } catch (Exception ex) {
                RequestShowInfo?.Invoke("打开目录失败", ex.Message);
            }
        }

        // ── 写入 ─────────────────────────────────────
        public async Task WriteVariableAsync (string variableId, string rawText) {
            if (string.IsNullOrWhiteSpace(variableId)) return;

            VariableItem v = _variables.Variables
                .FirstOrDefault(x => x != null && x.Id == variableId);
            if (v == null) {
                RequestShowInfo?.Invoke("写入失败", "变量不存在");
                return;
            }

            object value;
            string parseErr;
            if (!ValueParser.TryParse(v.DataType, rawText, out value, out parseErr)) {
                RequestShowInfo?.Invoke("写入失败", parseErr);
                return;
            }

            bool ok;
            try {
                ok = await _variables.WriteAsync(variableId, value, CancellationToken.None);
            } catch (Exception ex) {
                RequestShowInfo?.Invoke("写入失败", ex.Message);
                _log?.Error("Variable", "写入变量异常: " + v.Name, ex);
                return;
            }

            if (!ok) {
                string err = string.IsNullOrWhiteSpace(v.LastError) ? "写入未成功" : v.LastError;
                RequestShowInfo?.Invoke("写入失败", err);
            } else {
                _log?.Info("Variable", "写入成功: " + v.Name + " = " + rawText);
            }
        }

        // ── 辅助 ─────────────────────────────────────
        private bool EnsureDevice () {
            if (HasDeviceSelected) return true;
            RequestShowInfo?.Invoke("提示", "请先选择左侧设备");
            return false;
        }
    }
}