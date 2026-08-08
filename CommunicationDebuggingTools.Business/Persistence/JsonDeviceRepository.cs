using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Persistence {
    /// <summary>
    /// 设备列表 JSON 持久化
    /// </summary>
    public class JsonDeviceRepository : IDeviceRepository {
        private readonly string _filePath;

        public JsonDeviceRepository (string filePath) {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("路径不能为空", "filePath");

            _filePath = filePath;
        }

        public IList<DeviceInfo> LoadAll () {
            if (!File.Exists(_filePath))
                return new List<DeviceInfo>();

            string json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<DeviceInfo>();

            return Deserialize(json);
        }

        public void SaveAll (IList<DeviceInfo> devices) {
            if (devices == null)
                devices = new List<DeviceInfo>();

            string dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = Serialize(devices);
            File.WriteAllText(_filePath, json);
        }

        // ---------- 序列化（方案 A：.NET Framework 自带）----------

        private static string Serialize (IList<DeviceInfo> devices) {
            var ser = new JavaScriptSerializer();
            return ser.Serialize(devices);
        }

        private static IList<DeviceInfo> Deserialize (string json) {
            var ser = new JavaScriptSerializer();
            List<DeviceInfo> list = ser.Deserialize<List<DeviceInfo>>(json);
            return list ?? new List<DeviceInfo>();
        }
    }
}