using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Persistence {
    /// <summary>
    /// 基于本地 JSON 文件的设备列表持久化实现（<see cref="IDeviceRepository"/>）。
    /// 使用 .NET Framework 自带的 <see cref="JavaScriptSerializer"/>，避免额外引入第三方 JSON 库依赖。
    /// 写入采用"先写临时文件、再原子替换"的方式，降低程序异常退出/断电导致配置文件损坏的风险。
    /// </summary>
    public class JsonDeviceRepository : IDeviceRepository {
        /// <summary>JSON 配置文件的完整路径。</summary>
        private readonly string _filePath;

        /// <summary>
        /// 创建仓储实例。
        /// </summary>
        /// <param name="filePath">JSON 文件的完整路径，目录不存在时会在保存时自动创建。</param>
        /// <exception cref="ArgumentException">路径为空或空白时抛出。</exception>
        public JsonDeviceRepository (string filePath) {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("路径不能为空", "filePath");

            _filePath = filePath;
        }

        /// <summary>
        /// 加载全部设备配置。
        /// 文件不存在、内容为空或解析失败时，均返回空列表而不是抛出异常，
        /// 避免因为配置文件损坏导致整个程序无法启动。
        /// </summary>
        public IList<DeviceInfo> LoadAll () {
            if (!File.Exists(_filePath))
                return new List<DeviceInfo>();

            string json;
            try {
                json = File.ReadAllText(_filePath);
            } catch (IOException) {
                // 文件被占用/读取失败时，视为无数据，避免影响程序启动
                return new List<DeviceInfo>();
            }

            if (string.IsNullOrWhiteSpace(json))    
                return new List<DeviceInfo>();

            try {
                return Deserialize(json);
            } catch {
                // JSON 内容损坏/格式不兼容时，同样返回空列表兜底
                return new List<DeviceInfo>();
            }
        }

        /// <summary>
        /// 将设备列表整体保存到 JSON 文件（全量覆盖）。
        /// 先写入同目录下的临时文件，成功后再替换正式文件，避免写入过程中崩溃导致原文件被截断损坏。
        /// </summary>
        /// <param name="devices">待保存的设备列表；为 null 时按空列表处理。</param>
        public void SaveAll (IList<DeviceInfo> devices) {
            if (devices == null)
                devices = new List<DeviceInfo>();

            string dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = Serialize(devices);

            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_filePath))
                File.Delete(_filePath);
            File.Move(tempPath, _filePath);
        }

        // ---------- 序列化（方案 A：.NET Framework 自带）----------

        /// <summary>将设备列表序列化为 JSON 字符串。</summary>
        private static string Serialize (IList<DeviceInfo> devices) {
            var ser = new JavaScriptSerializer();
            return ser.Serialize(devices);
        }

        /// <summary>将 JSON 字符串反序列化为设备列表；解析结果为 null 时返回空列表。</summary>
        private static IList<DeviceInfo> Deserialize (string json) {
            var ser = new JavaScriptSerializer();
            List<DeviceInfo> list = ser.Deserialize<List<DeviceInfo>>(json);
            return list ?? new List<DeviceInfo>();
        }
    }
}