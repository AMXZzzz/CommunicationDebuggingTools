using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 设备管理页 ViewModel。
    /// 绑定到 DevicePage.DataContext；Page 只做 UI 路由（面板显示/隐藏）。
    ///
    /// 线程约定：所有公开方法和属性在 UI 线程调用。
    /// ConnectAllAsync 内部用 await 保持 UI 响应。
    /// </summary>
    public sealed class DevicePageViewModel : ViewModelBase {

        private readonly IDeviceService _devices;
        private readonly IAppLogger     _log;

        // ── 绑定属性 ──────────────────────────────────
        /// <summary>展示列表 = DeviceInfo 列表 + 末尾 AddDeviceMarker。</summary>
        public ObservableCollection<object> DisplayList { get; } =
            new ObservableCollection<object>();

        private bool _isSelectMode;
        /// <summary>是否处于多选删除模式。</summary>
        public bool IsSelectMode {
            get => _isSelectMode;
            set => SetField(ref _isSelectMode, value);
        }

        private int _deviceCount;
        public int DeviceCount {
            get => _deviceCount;
            private set => SetField(ref _deviceCount, value);
        }

        // ── 命令 ──────────────────────────────────────
        public ICommand ConnectAllCommand { get; }
        public ICommand DisconnectAllCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand EnterSelectModeCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelSelectCommand { get; }

        // ── 事件（View 订阅，处理面板显示/隐藏等纯 UI 操作）──
        public event Action                    RequestOpenAdd;
        public event Action<DeviceInfo>        RequestOpenEdit;
        public event Action<string>            RequestShowError;

        // ── 构造 ──────────────────────────────────────
        public DevicePageViewModel (IDeviceService devices, IAppLogger logger = null) {
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _log = logger;

            ConnectAllCommand = new RelayCommand(async () => await ConnectAllAsync());
            DisconnectAllCommand = new RelayCommand(DisconnectAll);
            RefreshCommand = new RelayCommand(Refresh);
            EnterSelectModeCommand = new RelayCommand(() => IsSelectMode = true);
            ConfirmDeleteCommand = new RelayCommand<IEnumerable<string>>(ConfirmDelete);
            CancelSelectCommand = new RelayCommand(() => IsSelectMode = false);

            _devices.Devices.CollectionChanged += (_, __) => RebuildDisplayList();
            RebuildDisplayList();
        }

        // ── 公开操作（View 调用或通过命令）───────────────
        public void OpenAdd () => RequestOpenAdd?.Invoke();

        public void OpenEdit (DeviceInfo info) {
            if (info != null) RequestOpenEdit?.Invoke(info);
        }

        public void SaveDevice (DeviceInfo info, bool isNew) {
            if (info == null) return;
            if (string.IsNullOrWhiteSpace(info.Name) || string.IsNullOrWhiteSpace(info.Protocol)) {
                RequestShowError?.Invoke("名称和协议不能为空");
                return;
            }
            try {
                if (isNew) _devices.Add(info);
                else _devices.Update(info);
                _log?.Info("Device", (isNew ? "新增" : "更新") + "设备: " + info.Name);
            } catch (Exception ex) {
                RequestShowError?.Invoke(ex.Message);
                _log?.Error("Device", "保存设备失败", ex);
            }
        }

        public void RemoveDevice (string id) {
            if (string.IsNullOrEmpty(id)) return;
            try {
                _devices.Remove(id);
                _log?.Info("Device", "删除设备: " + id);
            } catch (Exception ex) {
                RequestShowError?.Invoke(ex.Message);
                _log?.Error("Device", "删除设备失败", ex);
            }
        }

        // ── 内部操作 ──────────────────────────────────
        private async Task ConnectAllAsync () {
            var list = _devices.Devices
                .Where(d => d != null && !d.IsConnected)
                .ToList();

            foreach (DeviceInfo d in list) {
                d.StatusType = DeviceStatusType.Connecting;
                d.IsConnected = false;
            }

            foreach (DeviceInfo d in list) {
                try {
                    await _devices.ConnectAsync(d.Id, CancellationToken.None);
                } catch (Exception ex) {
                    d.IsConnected = false;
                    d.StatusType = DeviceStatusType.Error;
                    _log?.Error("Device", "连接失败: " + d.Name, ex);
                }
            }
        }

        private void DisconnectAll () {
            foreach (DeviceInfo d in _devices.Devices.ToList()) {
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                _devices.Disconnect(d.Id);
            }
        }

        private void Refresh () {
            _devices.Load();
            RebuildDisplayList();
            _log?.Info("Device", "已刷新设备列表");
        }

        private void ConfirmDelete (IEnumerable<string> ids) {
            if (ids == null) return;
            foreach (string id in ids.ToList())
                try { _devices.Remove(id); } catch { }
            IsSelectMode = false;
        }

        private void RebuildDisplayList () {
            DisplayList.Clear();
            foreach (DeviceInfo d in _devices.Devices)
                DisplayList.Add(d);
            DisplayList.Add(AddDeviceMarker.Instance);
            DeviceCount = _devices.Devices.Count;
        }
    }

    /// <summary>添加设备卡片占位标记（保留原有类型，不改动）。</summary>
    internal sealed class AddDeviceMarker {
        public static readonly AddDeviceMarker Instance = new AddDeviceMarker();
        private AddDeviceMarker () { }
    }
}