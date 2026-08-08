using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    /// <summary>
    /// 设备业务服务
    /// </summary>
    public class DeviceService : IDeviceService {
        private readonly IProtocolResolver _resolver;
        private readonly IDeviceRepository _repository;
        private readonly Dictionary<string, IProtocol> _sessions =
            new Dictionary<string, IProtocol>();

        public ObservableCollection<DeviceInfo> Devices { get; private set; }

        public DeviceService (IProtocolResolver resolver, IDeviceRepository repository) {
            if (resolver == null)
                throw new ArgumentNullException("resolver");
            if (repository == null)
                throw new ArgumentNullException("repository");

            _resolver = resolver;
            _repository = repository;
            Devices = new ObservableCollection<DeviceInfo>();
        }

        public void Load () {
            Devices.Clear();

            IList<DeviceInfo> list = _repository.LoadAll();
            if (list == null)
                return;

            foreach (DeviceInfo d in list) {
                // 启动时一律视为未连接
                d.IsConnected = false;
                if (d.StatusType == DeviceStatusType.Success ||
                    d.StatusType == DeviceStatusType.Connecting) {
                    d.StatusType = DeviceStatusType.Offline;
                }
                Devices.Add(d);
            }
        }

        public void Save () {
            _repository.SaveAll(Devices.ToList());
        }

        public void Add (DeviceInfo device) {
            if (device == null)
                throw new ArgumentNullException("device");

            if (string.IsNullOrEmpty(device.Id))
                device.Id = Guid.NewGuid().ToString("N");

            if (Devices.Any(d => d.Id == device.Id))
                throw new InvalidOperationException("设备 Id 已存在: " + device.Id);

            Devices.Add(device);
            Save();
        }

        public void Update (DeviceInfo device) {
            if (device == null)
                throw new ArgumentNullException("device");
            if (string.IsNullOrEmpty(device.Id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo old = Devices.FirstOrDefault(d => d.Id == device.Id);
            if (old == null)
                throw new InvalidOperationException("设备不存在: " + device.Id);

            // 已连接时改 IP/协议：先断开再更新
            if (old.IsConnected &&
                (old.Ip != device.Ip || old.Port != device.Port ||
                 old.Protocol != device.Protocol || old.UnitId != device.UnitId)) {
                Disconnect(old.Id);
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Offline;
            } else {
                device.IsConnected = old.IsConnected;
            }

            int index = Devices.IndexOf(old);
            Devices[index] = device;
            Save();
        }

        public void Remove (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo d = Devices.FirstOrDefault(x => x.Id == id);
            if (d == null)
                return;

            Disconnect(id);
            Devices.Remove(d);
            Save();
        }

        public bool Connect (string id) {
            DeviceInfo device = FindRequired(id);

            if (device.IsConnected)
                return true;

            device.StatusType = DeviceStatusType.Connecting;

            IProtocol protocol = _resolver.Resolve(device.Protocol);
            if (protocol == null) {
                device.StatusType = DeviceStatusType.Error;
                device.IsConnected = false;
                return false;
            }

            try {
                bool ok = protocol.Connect(device.Ip, device.Port, device.UnitId);
                if (ok) {
                    _sessions[id] = protocol;
                    device.IsConnected = true;
                    device.StatusType = DeviceStatusType.Success;
                } else {
                    SafeDisconnectProtocol(protocol);
                    device.IsConnected = false;
                    device.StatusType = DeviceStatusType.Error;
                }
                return ok;
            } catch {
                SafeDisconnectProtocol(protocol);
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Error;
                return false;
            }
        }

        public void Disconnect (string id) {
            if (string.IsNullOrEmpty(id))
                return;

            IProtocol protocol;
            if (_sessions.TryGetValue(id, out protocol)) {
                SafeDisconnectProtocol(protocol);
                _sessions.Remove(id);
            }

            DeviceInfo device = Devices.FirstOrDefault(d => d.Id == id);
            if (device != null) {
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Offline;
            }
        }

        public IProtocol GetProtocol (string deviceId) {
            if (string.IsNullOrEmpty(deviceId))
                return null;

            IProtocol p;
            if (_sessions.TryGetValue(deviceId, out p))
                return p;
            return null;
        }

        private DeviceInfo FindRequired (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo d = Devices.FirstOrDefault(x => x.Id == id);
            if (d == null)
                throw new InvalidOperationException("设备不存在: " + id);
            return d;
        }

        private static void SafeDisconnectProtocol (IProtocol protocol) {
            if (protocol == null)
                return;
            try { protocol.Disconnect(); } catch { }
        }
    }
}