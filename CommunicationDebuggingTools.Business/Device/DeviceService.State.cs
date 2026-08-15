using System;
using System.Linq;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    /// <summary>
    /// 设备运行时状态标记、字段拷贝、查找与安全断开。
    /// 不包含任何协议语义解析。
    /// </summary>
    public partial class DeviceService {

        /// <summary>加载/刷新后：清连接态；RUN/连接中 → 离线。</summary>
        private static void ResetRuntimeState (DeviceInfo d) {
            d.IsConnected = false;
            if (d.StatusType == DeviceStatusType.Success
                || d.StatusType == DeviceStatusType.Connecting)
                d.StatusType = DeviceStatusType.Offline;
            // 站号与扩展 JSON 兜底（旧配置缺字段时）
            if (d.StationNo < 0)
                d.StationNo = 1;
            if (string.IsNullOrWhiteSpace(d.ExtraSettingsJson))
                d.ExtraSettingsJson = "{}";
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

        /// <summary>连接相关配置是否变化（变化则需先断开再连）。</summary>
        private static bool IsConnectionConfigChanged (DeviceInfo old, DeviceInfo device) {
            return old.Ip != device.Ip
                || old.Port != device.Port
                || old.Protocol != device.Protocol
                || old.StationNo != device.StationNo
                || old.ExtraSettingsJson != device.ExtraSettingsJson;
        }

        /// <summary>可编辑字段写回同一实例，保留 Id 与运行时连接状态由调用方处理。</summary>
        private static void CopyDeviceFields (DeviceInfo source, DeviceInfo target) {
            target.Name = source.Name;
            target.Model = source.Model;
            target.Protocol = source.Protocol;
            target.Ip = source.Ip;
            target.Port = source.Port;
            target.StationNo = source.StationNo;
            target.ExtraSettingsJson = string.IsNullOrWhiteSpace(source.ExtraSettingsJson)
                ? "{}"
                : source.ExtraSettingsJson;
            target.Lane = source.Lane;
            target.ByteOrder = source.ByteOrder;
            target.WordOrder = source.WordOrder;
            target.StringEncoding = source.StringEncoding;
        }

        private DeviceInfo FindRequired (string id) {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Id 不能为空");
            DeviceInfo d = Devices.FirstOrDefault(x => x.Id == id);
            if (d == null)
                throw new InvalidOperationException("设备不存在: " + id);
            return d;
        }

        /// <summary>
        /// 安全释放协议实例：先断开会话，再 Dispose 托管资源。
        /// IProtocol : IDisposable，直接调 Dispose（实现内部会先 Disconnect）。
        /// </summary>
        private static void SafeDisconnectProtocol (IProtocol protocol) {
            if (protocol == null)
                return;
            try { protocol.Dispose(); } catch { }
        }
    }
}