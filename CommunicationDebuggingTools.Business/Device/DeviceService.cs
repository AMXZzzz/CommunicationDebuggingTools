using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    /// <summary>
    /// 设备业务服务：组合协议解析与持久化，对外提供 CRUD 与连接管理。
    /// 连接使用 <see cref="ProtocolConnectionContext"/>，私有参数只在 ProtocolSettingsJson。
    /// </summary>
    public class DeviceService : IDeviceService {
        private readonly IProtocolResolver _resolver;
        private readonly IDeviceRepository _repository;
        private readonly Dictionary<string, IProtocol> _sessions =
            new Dictionary<string, IProtocol>();
        private readonly Dictionary<string, CancellationTokenSource> _connectCts =
            new Dictionary<string, CancellationTokenSource>();

        // 后台心跳：每 3 秒检查一次已连接会话是否真的还活着
        private readonly Timer _heartbeat;
        // 构造时捕获 UI 同步上下文，用于将状态变更 Post 回 UI 线程
        private readonly SynchronizationContext _uiContext;

        public ObservableCollection<DeviceInfo> Devices { get; private set; }

        public DeviceService (IProtocolResolver resolver, IDeviceRepository repository) {
            if (resolver == null)
                throw new ArgumentNullException("resolver");
            if (repository == null)
                throw new ArgumentNullException("repository");

            _resolver = resolver;
            _repository = repository;
            Devices = new ObservableCollection<DeviceInfo>();

            // 捕获 UI 线程同步上下文（必须在 UI 线程上构造此服务）
            _uiContext = SynchronizationContext.Current;
            // 每 3 秒检测一次已连接会话，断线时自动标为离线
            _heartbeat = new Timer(HeartbeatCallback, null,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }


        /// <summary>仅测试：跳过探测，直接挂上已连接会话。</summary>
        internal void AttachSessionForTest (string deviceId, IProtocol protocol) {
            DeviceInfo d = FindRequired(deviceId);
            _sessions[deviceId] = protocol;
            d.IsConnected = true;
            d.StatusType = DeviceStatusType.Success;
        }

        // -------------------- 持久化 --------------------

        /// <summary>重新加载设备列表；先断开全部会话，状态一律离线。</summary>
        public void Load () {
            DisconnectAll();
            Devices.Clear();

            IList<DeviceInfo> list = _repository.LoadAll();
            if (list == null)
                return;

            foreach (DeviceInfo d in list) {
                ResetRuntimeState(d);
                Devices.Add(d);
            }
        }

        /// <summary>保存当前设备集合。</summary>
        public void Save () {
            _repository.SaveAll(Devices.ToList());
        }

        // -------------------- CRUD --------------------

        /// <summary>新增设备并立即持久化。</summary>
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
        /// 更新同一实例上的字段并持久化。
        /// 连接相关参数变化且仍在连接时，先断开再写回。
        /// </summary>
        public void Update (DeviceInfo device) {
            if (device == null)
                throw new ArgumentNullException("device");
            if (string.IsNullOrEmpty(device.Id))
                throw new ArgumentException("Id 不能为空");

            DeviceInfo old = Devices.FirstOrDefault(d => d.Id == device.Id);
            if (old == null)
                throw new InvalidOperationException("设备不存在: " + device.Id);

            if (old.IsConnected && IsConnectionConfigChanged(old, device))
                Disconnect(old.Id);

            CopyDeviceFields(device, old);
            Save();
        }

        /// <summary>断开、移除并持久化；不存在则忽略。</summary>
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

        // -------------------- 连接 --------------------

        /// <summary>
        /// 异步连接：探测 → 解析插件 → ProtocolConnectionContext → 建会话。
        /// </summary>
        public async Task<bool> ConnectAsync (string id, CancellationToken cancellationToken) {
            DeviceInfo device = FindRequired(id);
            if (device.IsConnected)
                return true;

            CancelConnect(id);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectCts[id] = linkedCts;
            CancellationToken ct = linkedCts.Token;

            MarkConnecting(device);

            IProtocol protocol = null;
            try {
                if (!await ProbeReachableAsync(device, ct)) {
                    MarkOffline(device);
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                protocol = _resolver.Resolve(device.Protocol);
                if (protocol == null) {
                    MarkError(device);
                    return false;
                }

                bool ok = await protocol.ConnectAsync(BuildConnectionContext(device), ct);

                if (ct.IsCancellationRequested) {
                    SafeDisconnectProtocol(protocol);
                    MarkOffline(device);
                    return false;
                }

                if (ok) {
                    _sessions[id] = protocol;
                    MarkConnected(device);
                } else {
                    SafeDisconnectProtocol(protocol);
                    MarkError(device);
                }

                return ok;
            } catch (OperationCanceledException) {
                SafeDisconnectProtocol(protocol);
                MarkOffline(device);
                return false;
            } catch {
                SafeDisconnectProtocol(protocol);
                MarkError(device);
                return false;
            } finally {
                CleanupConnectCts(id, linkedCts);
            }
        }

        /// <summary>取消进行中的连接，并释放已建立的会话。</summary>
        public void Disconnect (string id) {
            if (string.IsNullOrEmpty(id))
                return;

            CancelConnect(id);

            IProtocol protocol;
            if (_sessions.TryGetValue(id, out protocol)) {
                SafeDisconnectProtocol(protocol);
                _sessions.Remove(id);
            }

            DeviceInfo device = Devices.FirstOrDefault(d => d.Id == id);
            if (device != null)
                MarkOffline(device);
        }

        /// <summary>
        /// 后台心跳回调（Timer 线程）。
        /// 检查所有"已连接"会话的 IsConnected 属性；
        /// 若协议报告已断线，则 Post 回 UI 线程调用 Disconnect 更新状态。
        /// </summary>
        private void HeartbeatCallback (object state) {
            foreach (string id in _sessions.Keys.ToList()) {
                IProtocol protocol;
                if (!_sessions.TryGetValue(id, out protocol)) continue;
                if (protocol.IsConnected) continue;

                // 协议已断线，但 DeviceInfo 可能还显示 Connected
                DeviceInfo device = Devices.FirstOrDefault(d => d.Id == id);
                if (device == null || !device.IsConnected) continue;

                string capturedId = id;
                if (_uiContext != null)
                    _uiContext.Post(_ => Disconnect(capturedId), null);
                else
                    Disconnect(capturedId);
            }
        }

        /// <summary>断开全部设备及残留会话，并停止心跳定时器。</summary>
        public void DisconnectAll () {
            // 先停止心跳，防止 Disconnect 执行期间并发回调
            try { _heartbeat?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

            foreach (string id in Devices.Select(d => d.Id).Where(x => !string.IsNullOrEmpty(x)).ToList())
                Disconnect(id);

            foreach (string id in _sessions.Keys.ToList())
                Disconnect(id);

            foreach (string id in _connectCts.Keys.ToList())
                CancelConnect(id);
        }

        /// <summary>获取已连接的协议会话；未连接返回 null。</summary>
        public IProtocol GetProtocol (string deviceId) {
            if (string.IsNullOrEmpty(deviceId))
                return null;

            IProtocol p;
            return _sessions.TryGetValue(deviceId, out p) ? p : null;
        }

        // -------------------- 私有：连接辅助 --------------------

        private static ProtocolConnectionContext BuildConnectionContext (DeviceInfo device) {
            return new ProtocolConnectionContext {
                Ip = device.Ip,
                Port = device.Port,
                ProtocolSettingsJson = device.ProtocolSettingsJson,
                ByteOrder = device.ByteOrder,
                WordOrder = device.WordOrder,
                StringEncoding = device.StringEncoding,
                TimeoutMs = 3000
            };
        }

        private static async Task<bool> ProbeReachableAsync (DeviceInfo device, CancellationToken ct) {
            return await TcpProbe.IsPortOpenAsync(device.Ip, device.Port, 1000, ct);
        }

        private void CancelConnect (string id) {
            CancellationTokenSource cts;
            if (!_connectCts.TryGetValue(id, out cts))
                return;

            try { cts.Cancel(); } catch { }
            _connectCts.Remove(id);
            try { cts.Dispose(); } catch { }
        }

        private void CleanupConnectCts (string id, CancellationTokenSource linkedCts) {
            CancellationTokenSource existing;
            if (_connectCts.TryGetValue(id, out existing) && existing == linkedCts) {
                _connectCts.Remove(id);
                linkedCts.Dispose();
            }
        }

        // -------------------- 私有：状态 / 字段 --------------------

        private static void ResetRuntimeState (DeviceInfo d) {
            d.IsConnected = false;
            if (d.StatusType == DeviceStatusType.Success ||
                d.StatusType == DeviceStatusType.Connecting) {
                d.StatusType = DeviceStatusType.Offline;
            }
        }

        private static void MarkConnecting (DeviceInfo d) {
            d.StatusType = DeviceStatusType.Connecting;
            d.IsConnected = false;
        }

        private static void MarkConnected (DeviceInfo d) {
            d.IsConnected = true;
            d.StatusType = DeviceStatusType.Success;
        }

        private static void MarkOffline (DeviceInfo d) {
            d.IsConnected = false;
            d.StatusType = DeviceStatusType.Offline;
        }

        private static void MarkError (DeviceInfo d) {
            d.IsConnected = false;
            d.StatusType = DeviceStatusType.Error;
        }

        /// <summary>连接相关配置是否变化（含 ProtocolSettingsJson）。</summary>
        private static bool IsConnectionConfigChanged (DeviceInfo old, DeviceInfo device) {
            return old.Ip != device.Ip
                || old.Port != device.Port
                || old.Protocol != device.Protocol
                || old.ProtocolSettingsJson != device.ProtocolSettingsJson;
        }

        /// <summary>将 source 的可编辑字段写到 target（同一实例刷新绑定）。</summary>
        private static void CopyDeviceFields (DeviceInfo source, DeviceInfo target) {
            target.Name = source.Name;
            target.Model = source.Model;
            target.Protocol = source.Protocol;
            target.Ip = source.Ip;
            target.Port = source.Port;
            target.Lane = source.Lane;
            target.ByteOrder = source.ByteOrder;
            target.WordOrder = source.WordOrder;
            target.StringEncoding = source.StringEncoding;
            target.ProtocolSettingsJson = source.ProtocolSettingsJson;
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