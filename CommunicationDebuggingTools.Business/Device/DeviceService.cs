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

        // 连续通讯失败计数；达到阈值时标为 Error（ALARM），成功时归零
        private readonly Dictionary<string, int> _commErrors =
            new Dictionary<string, int>();
        private const int COMM_ERROR_THRESHOLD = 3;

        // PingAsync 回调需要 Post 回 UI 线程
        private readonly System.Threading.SynchronizationContext _uiContext;

        public ObservableCollection<DeviceInfo> Devices { get; private set; }

        public DeviceService (IProtocolResolver resolver, IDeviceRepository repository) {
            if (resolver == null)
                throw new ArgumentNullException("resolver");
            if (repository == null)
                throw new ArgumentNullException("repository");

            _resolver = resolver;
            _repository = repository;
            Devices = new ObservableCollection<DeviceInfo>();
            _uiContext = System.Threading.SynchronizationContext.Current;
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
        /// 通讯成功：清零连续失败计数，若设备处于 Error 状态则恢复为 Success。
        /// 在 UI 线程（VariableService 回调）中调用。
        /// </summary>
        public void ReportCommSuccess (string deviceId) {
            if (string.IsNullOrEmpty(deviceId)) return;
            _commErrors[deviceId] = 0;

            DeviceInfo device = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device != null && device.IsConnected
                    && device.StatusType == DeviceStatusType.Error)
                device.StatusType = DeviceStatusType.Success;
        }

        /// <summary>
        /// 通讯失败：累加连续失败计数。
        /// 达到 <see cref="COMM_ERROR_THRESHOLD"/> 次后将设备标为 Error（ALARM）。
        /// TCP 断线时直接调 <see cref="Disconnect"/>，无需走此方法。
        /// 在 UI 线程（VariableService 回调）中调用。
        /// </summary>
        public void ReportCommError (string deviceId) {
            if (string.IsNullOrEmpty(deviceId)) return;

            int count;
            _commErrors.TryGetValue(deviceId, out count);
            _commErrors[deviceId] = ++count;

            if (count < COMM_ERROR_THRESHOLD) return;

            DeviceInfo device = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device != null && device.IsConnected
                    && device.StatusType != DeviceStatusType.Error)
                device.StatusType = DeviceStatusType.Error;
        }

        /// <summary>
        /// 检查所有已连接会话（DispatcherTimer 每 3 秒在 UI 线程上调用）。
        /// 直接调 PingAsync —— 各协议内部已合并了 TCP 层（Socket.Poll）
        /// 和协议层（实际读请求）两层检测，此处无需重复判断。
        /// </summary>
        public void CheckConnections () {
            foreach (string id in _sessions.Keys.ToList()) {
                IProtocol protocol;
                if (!_sessions.TryGetValue(id, out protocol)) continue;

                string capturedId = id;
                IProtocol capturedProto = protocol;

                // 在后台线程执行 PingAsync（含真实 I/O），完成后回 UI 线程处理结果
                System.Threading.Tasks.Task.Run(async () => {
                    bool ok = await capturedProto.PingAsync(
                        System.Threading.CancellationToken.None);

                    void handle () => OnPingResult(capturedId, capturedProto, ok);

                    if (_uiContext != null) _uiContext.Post(_ => handle(), null);
                    else handle();
                });
            }
        }

        /// <summary>
        /// PingAsync 回调，在 UI 线程执行。
        /// ok=true  → 通讯正常，清零错误计数，恢复 RUN。
        /// ok=false → 再次查 IsConnected 区分「TCP 断线」和「通讯异常」：
        ///            断线 → Disconnect（OFFLINE）；通讯异常 → ReportCommError（累计到 ALARM）。
        /// </summary>
        private void OnPingResult (string deviceId, IProtocol protocol, bool ok) {
            if (ok) {
                ReportCommSuccess(deviceId);
                return;
            }
            // PingAsync 返回 false：TCP 断线 or 协议层失败
            if (!protocol.IsConnected)
                Disconnect(deviceId);         // TCP 断线 → OFFLINE
            else
                ReportCommError(deviceId);    // 协议层失败 → 计数，达阈值 → ALARM
        }

        /// <summary>断开全部设备及残留会话。</summary>
        public void DisconnectAll () {
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