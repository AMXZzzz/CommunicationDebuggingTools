using System;
using System.Linq;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Device {
    /// <summary>状态标记、字段拷贝、查找与安全断开。</summary>
    public partial class DeviceService {
        private static void ResetRuntimeState (DeviceInfo d) {
            d.IsConnected = false;
            if (d.StatusType == DeviceStatusType.Success
                || d.StatusType == DeviceStatusType.Connecting)
                d.StatusType = DeviceStatusType.Offline;
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

        private static bool IsConnectionConfigChanged (DeviceInfo old, DeviceInfo device) {
            return old.Ip != device.Ip
                || old.Port != device.Port
                || old.Protocol != device.Protocol
                || old.ProtocolSettingsJson != device.ProtocolSettingsJson;
        }

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