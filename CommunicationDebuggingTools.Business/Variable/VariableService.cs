using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Variable {
    /// <summary>
    /// 变量业务：配置持久化 + 通过设备会话做协议读写。
    /// </summary>
    public class VariableService : IVariableService {
        private readonly IDeviceService _devices;
        private readonly IVariableRepository _repository;

        public ObservableCollection<VariableItem> Variables { get; private set; }

        public VariableService (IDeviceService devices, IVariableRepository repository) {
            if (devices == null) throw new ArgumentNullException(nameof(devices));
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            _devices = devices;
            _repository = repository;
            Variables = new ObservableCollection<VariableItem>();
        }

        public void Load () {
            Variables.Clear();
            IList<VariableItem> list = _repository.LoadAll();
            if (list == null) return;
            foreach (VariableItem v in list)
                Variables.Add(v);
        }

        public void Save () =>
            _repository.SaveAll(Variables.ToList());

        public void Add (VariableItem item) {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(item.Id))
                item.Id = Guid.NewGuid().ToString("N");
            if (Variables.Any(x => x.Id == item.Id))
                throw new InvalidOperationException("变量 Id 已存在: " + item.Id);

            Variables.Add(item);
            Save();
        }

        public void Update (VariableItem item) {
            if (item == null) throw new ArgumentNullException(nameof(item));
            VariableItem old = FindRequired(item.Id);

            old.DeviceId = item.DeviceId;
            old.Name = item.Name;
            old.Address = item.Address;
            old.DataType = item.DataType;
            old.Access = item.Access;
            old.Length = item.Length;
            Save();
        }

        public void Remove (string id) {
            VariableItem v = Variables.FirstOrDefault(x => x.Id == id);
            if (v == null) return;
            Variables.Remove(v);
            Save();
        }

        /// <summary>读一点：权限 → 设备 → 协议 → 回填 LastValue。</summary>
        public async Task<bool> ReadAsync (string variableId, CancellationToken cancellationToken) {
            VariableItem v = FindRequired(variableId);

            if (v.Access == VariableAccess.WriteOnly) {
                v.LastError = "只写变量不可读";
                v.Quality = DataQuality.Bad;
                return false;
            }

            IProtocolDataAccess access;
            DeviceInfo device;
            if (!TryGetDataAccess(v.DeviceId, out access, out device, out string err)) {
                v.LastError = err;
                v.Quality = DataQuality.Bad;
                return false;
            }

            ProtocolDataMessage msg = BuildMessage(v, device, null);
            ProtocolDataMessage result = await access.ReadAsync(msg, cancellationToken);

            v.LastError = result.ErrorMessage ?? "";
            v.Quality = result.Quality;
            if (result.Success) {
                v.LastValue = result.Value;
                return true;
            }
            return false;
        }

        /// <summary>写一点：权限 → 协议 → 成功则更新 LastValue。</summary>
        public async Task<bool> WriteAsync (
            string variableId, object value, CancellationToken cancellationToken) {
            VariableItem v = FindRequired(variableId);

            if (v.Access == VariableAccess.ReadOnly) {
                v.LastError = "只读变量不可写";
                v.Quality = DataQuality.Bad;
                return false;
            }

            IProtocolDataAccess access;
            DeviceInfo device;
            if (!TryGetDataAccess(v.DeviceId, out access, out device, out string err)) {
                v.LastError = err;
                v.Quality = DataQuality.Bad;
                return false;
            }

            ProtocolDataMessage msg = BuildMessage(v, device, value);
            ProtocolDataMessage result = await access.WriteAsync(msg, cancellationToken);

            v.LastError = result.ErrorMessage ?? "";
            if (result.Success) {
                v.LastValue = value;
                v.Quality = DataQuality.Good;
                return true;
            }

            v.Quality = DataQuality.Bad;
            return false;
        }

        public async Task ReadByDeviceAsync (string deviceId, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(deviceId)) return;

            foreach (VariableItem v in Variables.Where(x => x.DeviceId == deviceId).ToList()) {
                if (v.Access == VariableAccess.WriteOnly)
                    continue;
                await ReadAsync(v.Id, cancellationToken);
            }
        }

        // -------------------- 私有 --------------------

        private bool TryGetDataAccess (
            string deviceId,
            out IProtocolDataAccess access,
            out DeviceInfo device,
            out string error) {
            access = null;
            device = null;
            error = "";

            device = _devices.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null) {
                error = "设备不存在";
                return false;
            }
            if (!device.IsConnected) {
                error = "设备未连接";
                return false;
            }

            IProtocol protocol = _devices.GetProtocol(deviceId);
            access = protocol as IProtocolDataAccess;
            if (access == null) {
                error = "协议不支持数据读写";
                return false;
            }
            return true;
        }

        /// <summary>组共性报文；序与编码取设备默认。</summary>
        private static ProtocolDataMessage BuildMessage (
            VariableItem v, DeviceInfo device, object writeValue) {
            return new ProtocolDataMessage {
                Address = v.Address ?? "",
                DataType = v.DataType,
                Length = v.Length,
                ByteOrder = device.ByteOrder,
                WordOrder = device.WordOrder,
                StringEncoding = device.StringEncoding,
                Value = writeValue
            };
        }

        private VariableItem FindRequired (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");
            VariableItem v = Variables.FirstOrDefault(x => x.Id == id);
            if (v == null)
                throw new InvalidOperationException("变量不存在: " + id);
            return v;
        }
    }
}