using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    /// <summary>
    /// 设备业务服务的默认实现。
    /// 组合 IProtocolResolver（协议插件解析）与 IDeviceRepository（持久化），
    /// 对外提供设备的增删改查、连接管理，是 UI 层与底层通信/存储之间的唯一入口。
    /// </summary>
    public class DeviceService : IDeviceService {
        /// <summary>协议插件解析器，用于按协议名称创建协议实例。</summary>
        private readonly IProtocolResolver _resolver;

        /// <summary>设备配置持久化仓储。</summary>
        private readonly IDeviceRepository _repository;

        /// <summary>
        /// 设备 Id 到当前已建立连接的协议会话的映射。
        /// 只有连接成功的设备才会出现在此字典中，断开/连接失败会及时移除。
        /// </summary>
        private readonly Dictionary<string, IProtocol> _sessions =
            new Dictionary<string, IProtocol>();

        /// <summary>
        /// 当前已加载的设备集合，供 UI 直接绑定。集合内容变化（增/删/改）会自动驱动界面刷新。
        /// </summary>
        public ObservableCollection<DeviceInfo> Devices { get; private set; }

        /// <summary>
        /// 创建设备服务实例。
        /// </summary>
        /// <param name="resolver">协议插件解析器，不能为 null。</param>
        /// <param name="repository">设备持久化仓储，不能为 null。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null 时抛出。</exception>
        public DeviceService (IProtocolResolver resolver, IDeviceRepository repository) {
            if (resolver == null)
                throw new ArgumentNullException("resolver");
            if (repository == null)
                throw new ArgumentNullException("repository");

            _resolver = resolver;
            _repository = repository;
            Devices = new ObservableCollection<DeviceInfo>();
        }

        /// <summary>
        /// 从持久化存储重新加载全部设备，覆盖当前 Devices 集合。
        /// 加载后会强制将所有设备的连接状态重置为离线，因为程序刚启动时不可能存在真实的通信连接，
        /// 避免界面显示上次保存时的连接状态这种误导性信息。
        /// </summary>
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

        /// <summary>
        /// 将当前 Devices 集合整体保存到持久化存储。
        /// </summary>
        public void Save () {
            _repository.SaveAll(Devices.ToList());
        }

        /// <summary>
        /// 新增一个设备。若未指定 Id 会自动生成；新增成功后立即持久化。
        /// </summary>
        /// <param name="device">新设备信息，不能为 null。</param>
        /// <exception cref="ArgumentNullException">device 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">Id 已存在时抛出。</exception>
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

        /// <summary>
        /// 根据 DeviceInfo.Id 更新已有设备的配置，更新后立即持久化。
        /// 若设备当前已连接，且本次修改了 IP / 端口 / 协议 / 站号等连接相关参数，
        /// 会先自动断开旧连接，避免出现参数已变但连接仍使用旧参数的不一致状态。
        /// </summary>
        /// <param name="device">包含新值的设备信息，不能为 null 且 Id 不能为空。</param>
        /// <exception cref="ArgumentNullException">device 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">Id 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">对应 Id 的设备不存在时抛出。</exception>
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

        /// <summary>
        /// 删除指定 Id 的设备：先断开连接释放资源，再从集合中移除并持久化。
        /// 若设备不存在则静默忽略（不抛异常），保证删除操作是幂等的。
        /// </summary>
        /// <param name="id">设备唯一标识。</param>
        /// <exception cref="ArgumentException">id 为空时抛出。</exception>
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

        /// <summary>
        /// 建立与指定设备的通信连接。
        /// 流程：根据设备的 Protocol 名称向解析器解析出协议实例，再调用协议的 Connect 方法真正建立连接；
        /// 连接过程中的异常会被捕获并转换为失败结果，不会向上抛出，避免单个设备连接异常导致整个 UI 崩溃。
        /// 若设备已处于连接状态，直接返回 true（幂等）。
        /// </summary>
        /// <param name="id">设备唯一标识。</param>
        /// <returns>连接是否成功。</returns>
        /// <exception cref="ArgumentException">id 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">设备不存在时抛出。</exception>
        public bool Connect (string id) {
            DeviceInfo device = FindRequired(id);

            if (device.IsConnected)
                return true;

            device.StatusType = DeviceStatusType.Connecting;

            IProtocol protocol = _resolver.Resolve(device.Protocol);
            if (protocol == null) {
                // 找不到对应协议插件（如插件未部署/名称拼写不一致）
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
                // 网络异常等不可预期错误：按连接失败处理，同时确保底层资源已释放
                SafeDisconnectProtocol(protocol);
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Error;
                return false;
            }
        }

        /// <summary>
        /// 断开与指定设备的通信连接并释放底层协议资源。
        /// 若设备当前未连接，则只会同步一次状态（不会抛异常），保证操作幂等。
        /// </summary>
        /// <param name="id">设备唯一标识；为空时直接忽略。</param>
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

        /// <summary>
        /// 获取指定设备当前的协议会话实例（仅在已成功连接时存在）。
        /// </summary>
        /// <param name="deviceId">设备唯一标识。</param>
        /// <returns>协议实例；未连接或设备不存在时返回 null。</returns>
        public IProtocol GetProtocol (string deviceId) {
            if (string.IsNullOrEmpty(deviceId))
                return null;

            IProtocol p;
            if (_sessions.TryGetValue(deviceId, out p))
                return p;
            return null;
        }

        /// <summary>
        /// 根据 Id 查找设备，找不到时抛出异常（供内部要求设备必须存在的场景使用）。
        /// </summary>
        private DeviceInfo FindRequired (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo d = Devices.FirstOrDefault(x => x.Id == id);
            if (d == null)
                throw new InvalidOperationException("设备不存在: " + id);
            return d;
        }

        /// <summary>
        /// 安全断开协议连接：吞掉断开过程中可能出现的异常，
        /// 避免清理资源这一操作本身再引发新的未处理异常。
        /// </summary>
        private static void SafeDisconnectProtocol (IProtocol protocol) {
            if (protocol == null)
                return;
            try { protocol.Disconnect(); } catch { }
        }
    }
}