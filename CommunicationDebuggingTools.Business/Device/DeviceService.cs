using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    public partial class DeviceService : IDeviceService {
        private readonly IProtocolResolver _resolver;
        private readonly IDeviceRepository _repository;
        private readonly IAppLogger _log;

        private readonly ConcurrentDictionary<string, IProtocol> _sessions =
            new ConcurrentDictionary<string, IProtocol>();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _connectCts =
            new ConcurrentDictionary<string, CancellationTokenSource>();
        private readonly ConcurrentDictionary<string, int> _commErrors =
            new ConcurrentDictionary<string, int>();

        private const int COMM_ERROR_THRESHOLD = 3;

        private readonly SynchronizationContext _uiContext;
        private int _pinging;
        private CancellationTokenSource _pingCts;

        public ObservableCollection<DeviceInfo> Devices { get; private set; }

        public DeviceService (
            IProtocolResolver resolver,
            IDeviceRepository repository,
            IAppLogger logger = null) {
            if (resolver == null) throw new ArgumentNullException("resolver");
            if (repository == null) throw new ArgumentNullException("repository");

            _resolver = resolver;
            _repository = repository;
            _log = logger;
            Devices = new ObservableCollection<DeviceInfo>();
            _uiContext = SynchronizationContext.Current;
        }

        private void LogInfo (string msg) {
            if (_log != null) _log.Info("Device", msg);
        }
        private void LogWarn (string msg) {
            if (_log != null) _log.Warn("Device", msg);
        }
        private void LogError (string msg) {
            if (_log != null) _log.Error("Device", msg);
        }

        public void Load () {
            try { if (_pingCts != null) _pingCts.Cancel(); } catch { }
            DisconnectAll();
            Devices.Clear();
            IList<DeviceInfo> list = _repository.LoadAll();
            if (list == null) return;
            foreach (DeviceInfo d in list) {
                ResetRuntimeState(d);
                Devices.Add(d);
            }
            LogInfo("已加载设备 " + Devices.Count + " 台");
        }

        public void Save () {
            _repository.SaveAll(Devices.ToList());
        }

        public void Add (DeviceInfo device) {
            if (device == null) throw new ArgumentNullException("device");
            if (string.IsNullOrEmpty(device.Id))
                device.Id = Guid.NewGuid().ToString("N");
            if (Devices.Any(d => d.Id == device.Id))
                throw new InvalidOperationException("设备 Id 已存在: " + device.Id);
            Devices.Add(device);
            Save();
            LogInfo("新增设备: " + device.Name);
        }

        public void Update (DeviceInfo device) {
            if (device == null) throw new ArgumentNullException("device");
            if (string.IsNullOrEmpty(device.Id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo old = Devices.FirstOrDefault(d => d.Id == device.Id);
            if (old == null)
                throw new InvalidOperationException("设备不存在: " + device.Id);

            if (old.IsConnected && IsConnectionConfigChanged(old, device))
                Disconnect(old.Id);

            CopyDeviceFields(device, old);
            Save();
            LogInfo("更新设备: " + old.Name);
        }

        public void Remove (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo d = Devices.FirstOrDefault(x => x.Id == id);
            if (d == null) return;

            string name = d.Name;
            Disconnect(id);
            Devices.Remove(d);
            Save();
            LogInfo("删除设备: " + name);
        }
    }
}