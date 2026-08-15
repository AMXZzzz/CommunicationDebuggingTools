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
using CommunicationDebuggingTools.Views.Pages.Device;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 设备管理页 ViewModel。
    /// 绑定到 DevicePage.DataContext；Page 只做 UI 路由（面板显示/隐藏）。
    /// </summary>
    public sealed class DevicePageViewModel : ViewModelBase {

        private readonly IDeviceService _devices;
        private readonly IAppLogger _log;

        /// <summary>
        /// 供 View 给 DataTemplate 生成的 DeviceCard 属性注入，避免卡片再走服务定位器。
        /// </summary>
        public IDeviceService Devices => _devices;

        /// <summary>展示列表 = DeviceInfo 列表 + 末尾 AddDeviceMarker。</summary>
        public ObservableCollection<object> DisplayList { get; } =
            new ObservableCollection<object>();

        private bool _isSelectMode;
        public bool IsSelectMode {
            get => _isSelectMode;
            set => SetField(ref _isSelectMode, value);
        }

        private int _deviceCount;
        public int DeviceCount {
            get => _deviceCount;
            private set => SetField(ref _deviceCount, value);
        }

        public ICommand ConnectAllCommand { get; }
        public ICommand DisconnectAllCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand EnterSelectModeCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelSelectCommand { get; }

        public event Action RequestOpenAdd;
        public event Action<DeviceInfo> RequestOpenEdit;
        public event Action<string> RequestShowError;

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
            // 使用 Views 命名空间下的占位类型，与 XAML DataTemplate 一致
            DisplayList.Add(AddDeviceMarker.Instance);
            DeviceCount = _devices.Devices.Count;
        }
    }
}