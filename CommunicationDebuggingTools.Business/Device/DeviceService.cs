using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Business.Tools;

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
        /// 设备 Id 到当前正在进行的 ConnectAsync 所对应取消令牌源的映射。
        /// 用于在“取消/断开”时真正中止尚未完成的连接过程，而不仅仅是重置界面状态。
        /// </summary>

        private readonly Dictionary<string, CancellationTokenSource> _connectCts =
            new Dictionary<string, CancellationTokenSource>();
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

            DisconnectAll();
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

            // 连接参数变了且仍在连接 → 先断
            bool connChanged =  old.Ip != device.Ip ||
                                old.Port != device.Port ||
                                old.Protocol != device.Protocol ||
                                old.UnitId != device.UnitId;

            if (old.IsConnected && connChanged) {
                Disconnect(old.Id);
                // Disconnect 已把 old 标成离线
            }

            // 写回同一实例（触发 INPC，卡片自动刷新）
            old.Name = device.Name;
            old.Model = device.Model;
            old.Protocol = device.Protocol;
            old.Ip = device.Ip;
            old.Port = device.Port;
            old.UnitId = device.UnitId;
            old.Lane = device.Lane;
            // 不要用 device 替换 old；不要改 Id

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
        /// 异步连接指定设备。
        /// 已连接时直接返回 true；连接失败或异常时更新设备状态并返回 false，不向上抛出异常。
        /// </summary>
        /// <param name="id">设备唯一标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否连接成功。</returns>
        /// <exception cref="ArgumentException">id 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">设备不存在时抛出。</exception>
        public async Task<bool> ConnectAsync (string id, CancellationToken cancellationToken) {
            DeviceInfo device = FindRequired(id);

            if (device.IsConnected)
                return true;

            // 取消该设备上一次未完成的连接
            CancelConnect(id);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectCts[id] = linkedCts;
            CancellationToken ct = linkedCts.Token;

            device.StatusType = DeviceStatusType.Connecting;
            device.IsConnected = false;

            IProtocol protocol = null;

            try {
                bool reachable = await TcpProbe.IsPortOpenAsync(
            device.Ip, device.Port, 1000, ct);

                if (!reachable) {
                    device.IsConnected = false;
                    device.StatusType = DeviceStatusType.Offline;
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                protocol = _resolver.Resolve(device.Protocol);
                if (protocol == null) {
                    device.IsConnected = false;
                    device.StatusType = DeviceStatusType.Error;
                    return false;
                }

                bool ok = await protocol.ConnectAsync(
            device.Ip, device.Port, device.UnitId, ct);

                // 用户已点取消：不写入 session、不标成功
                if (ct.IsCancellationRequested) {
                    SafeDisconnectProtocol(protocol);
                    device.IsConnected = false;
                    device.StatusType = DeviceStatusType.Offline;
                    return false;
                }

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
            } catch (OperationCanceledException) {
                if (protocol != null)
                    SafeDisconnectProtocol(protocol);
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Offline;
                return false;
            } catch {
                if (protocol != null)
                    SafeDisconnectProtocol(protocol);
                device.IsConnected = false;
                device.StatusType = DeviceStatusType.Error;
                return false;
            } finally {
                CancellationTokenSource existing;
                if (_connectCts.TryGetValue(id, out existing) && existing == linkedCts) {
                    _connectCts.Remove(id);
                    linkedCts.Dispose();
                }
            }
        }

        /// <summary>
        /// 取消指定设备当前正在进行的连接操作（若有），并释放对应的取消令牌源。
        /// </summary>
        /// <param name="id"></param>
        private void CancelConnect (string id) {
            CancellationTokenSource cts;
            if (!_connectCts.TryGetValue(id, out cts))
                return;

            try { cts.Cancel(); } catch { }

            _connectCts.Remove(id);

            try { cts.Dispose(); } catch { }
        }

        /// <summary>
        /// 断开与指定设备的通信连接并释放底层协议资源。
        /// 若设备当前未连接，则只会同步一次状态（不会抛异常），保证操作幂等。
        /// </summary>
        /// <param name="id">设备唯一标识；为空时直接忽略。</param>
        public void Disconnect (string id) {
            if (string.IsNullOrEmpty(id))
                return;

            // 若该设备当前有正在进行的连接尝试，先取消它，确保"取消"操作真正生效
            CancelConnect(id);

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
        /// 一次性断开所有设备的通信连接并释放底层协议资源。
        /// </summary>
        public void DisconnectAll () {
            foreach (string id in Devices.Select(d => d.Id).Where(x => !string.IsNullOrEmpty(x)).ToList())
                Disconnect(id);

            // 兜底：集合里没有、但 session/cts 里还有的
            foreach (string id in _sessions.Keys.ToList())
                Disconnect(id);
            foreach (string id in _connectCts.Keys.ToList())
                CancelConnect(id);
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