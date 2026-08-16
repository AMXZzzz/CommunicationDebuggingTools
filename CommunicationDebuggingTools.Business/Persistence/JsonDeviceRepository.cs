using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Persistence {
    /// <summary>
    /// 本地 JSON 设备仓储。
    /// 文件格式 version=1：{ "version":1, "devices":[ ... ] }。
    /// 兼容旧版根数组；加载时忽略未知字段；运行时状态不落盘（由 DeviceService 复位）。
    /// </summary>
    public class JsonDeviceRepository : IDeviceRepository {
        public const int CurrentVersion = 1;

        private readonly string _filePath;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public JsonDeviceRepository (string filePath) {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("路径不能为空", "filePath");
            _filePath = filePath;
        }

        public IList<DeviceInfo> LoadAll () {
            if (!File.Exists(_filePath))
                return new List<DeviceInfo>();

            string json;
            try {
                json = File.ReadAllText(_filePath);
            } catch (Exception ex) {
                Trace.TraceWarning("读取设备配置失败: {0}", ex.Message);
                return new List<DeviceInfo>();
            }

            if (string.IsNullOrWhiteSpace(json))
                return new List<DeviceInfo>();

            try {
                return ParseDocument(json);
            } catch (Exception ex) {
                Trace.TraceWarning("解析设备配置失败: {0}", ex.Message);
                return new List<DeviceInfo>();
            }
        }

        public void SaveAll (IList<DeviceInfo> devices) {
            if (devices == null)
                devices = new List<DeviceInfo>();

            string dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var doc = new DeviceFileDocument {
                version = CurrentVersion,
                devices = new List<DeviceInfo>(devices)
            };
            string json = _serializer.Serialize(doc);

            string tempPath = _filePath + ".tmp";
            try {
                File.WriteAllText(tempPath, json);
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(tempPath, _filePath);
            } catch (Exception ex) {
                Trace.TraceError("保存设备配置失败: {0}", ex.Message);
                try {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                } catch { }
            }
        }

        /// <summary>
        /// 支持：根数组（旧）或 { version, devices }（新）。
        /// </summary>
        private IList<DeviceInfo> ParseDocument (string json) {
            object root = _serializer.DeserializeObject(json);
            if (root == null)
                return new List<DeviceInfo>();

            // 旧格式：数组
            if (root is object[] arr) {
                return MapArray(arr);
            }

            // 新格式：字典
            if (root is Dictionary<string, object> map) {
                object devicesObj;
                if (!map.TryGetValue("devices", out devicesObj) || devicesObj == null)
                    return new List<DeviceInfo>();
                if (devicesObj is object[] devicesArr)
                    return MapArray(devicesArr);
                // 有时反序列化为 ArrayList
                if (devicesObj is ArrayList list) {
                    var tmp = new object[list.Count];
                    list.CopyTo(tmp);
                    return MapArray(tmp);
                }
            }

            return new List<DeviceInfo>();
        }

        private IList<DeviceInfo> MapArray (object[] arr) {
            var result = new List<DeviceInfo>();
            if (arr == null) return result;
            foreach (object item in arr) {
                DeviceInfo d = MapDevice(item as Dictionary<string, object>);
                if (d != null)
                    result.Add(d);
            }
            return result;
        }

        /// <summary>
        /// 手工映射，忽略 ProtocolSettingsJson / UnitId 等历史字段。
        /// </summary>
        private static DeviceInfo MapDevice (Dictionary<string, object> m) {
            if (m == null) return null;

            var d = new DeviceInfo();
            d.Id = GetString(m, "Id") ?? Guid.NewGuid().ToString("N");
            d.Name = GetString(m, "Name") ?? "新设备";
            d.Model = GetString(m, "Model") ?? "";
            d.Protocol = GetString(m, "Protocol") ?? "";
            d.Ip = GetString(m, "Ip") ?? "";
            d.Port = GetInt(m, "Port", 502);
            d.StationNo = GetInt(m, "StationNo", GetInt(m, "UnitId", 1));
            d.ExtraSettingsJson = GetString(m, "ExtraSettingsJson");
            if (string.IsNullOrWhiteSpace(d.ExtraSettingsJson))
                d.ExtraSettingsJson = "{}";

            // 不恢复 IsConnected / StatusType —— 由业务层 ResetRuntimeState
            d.IsConnected = false;
            d.StatusType = CommunicationDebuggingTools.Core.Enums.DeviceStatusType.Offline;

            int lane;
            if (TryGetInt(m, "Lane", out lane))
                d.Lane = (CommunicationDebuggingTools.Core.Enums.LaneType)lane;

            int bo;
            if (TryGetInt(m, "ByteOrder", out bo))
                d.ByteOrder = (CommunicationDebuggingTools.Core.Enums.ByteOrder)bo;
            int wo;
            if (TryGetInt(m, "WordOrder", out wo))
                d.WordOrder = (CommunicationDebuggingTools.Core.Enums.WordOrder)wo;
            int se;
            if (TryGetInt(m, "StringEncoding", out se))
                d.StringEncoding = (CommunicationDebuggingTools.Core.Enums.StringEncodingKind)se;

            return d;
        }

        private static string GetString (Dictionary<string, object> m, string key) {
            object v;
            if (!m.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static int GetInt (Dictionary<string, object> m, string key, int defaultValue) {
            int v;
            return TryGetInt(m, key, out v) ? v : defaultValue;
        }

        private static bool TryGetInt (Dictionary<string, object> m, string key, out int value) {
            value = 0;
            object v;
            if (!m.TryGetValue(key, out v) || v == null) return false;
            try {
                value = Convert.ToInt32(v);
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>落盘文档结构（version + devices）。</summary>
        public class DeviceFileDocument {
            public int version { get; set; }
            public List<DeviceInfo> devices { get; set; }
        }
    }
}
