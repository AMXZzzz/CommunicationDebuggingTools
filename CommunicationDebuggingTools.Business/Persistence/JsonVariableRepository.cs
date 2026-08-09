using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Persistence {
    /// <summary>
    /// 变量配置 JSON 文件仓储（与设备仓储同一风格）。
    /// 运行时 LastValue / Quality / LastError 不强制持久化；重载后质量为 Bad。
    /// </summary>
    public class JsonVariableRepository : IVariableRepository {
        private readonly string _filePath;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        /// <param name="filePath">完整路径；为空则使用程序目录下 variables.json。</param>
        public JsonVariableRepository (string filePath = null) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                _filePath = Path.Combine(dir, "variables.json");
            } else {
                _filePath = filePath;
            }
        }

        /// <summary>从磁盘加载；文件不存在或损坏时返回空列表。</summary>
        public IList<VariableItem> LoadAll () {
            try {
                if (!File.Exists(_filePath))
                    return new List<VariableItem>();

                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<VariableItem>();

                // 使用 DTO 避免反序列化事件字段等问题
                List<VariableDto> dtos = _serializer.Deserialize<List<VariableDto>>(json);
                if (dtos == null)
                    return new List<VariableItem>();

                var list = new List<VariableItem>();
                foreach (VariableDto d in dtos) {
                    if (d == null) continue;
                    list.Add(FromDto(d));
                }
                return list;
            } catch {
                return new List<VariableItem>();
            }
        }

        /// <summary>整体覆盖写入。</summary>
        public void SaveAll (IList<VariableItem> items) {
            try {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var dtos = new List<VariableDto>();
                if (items != null) {
                    foreach (VariableItem v in items) {
                        if (v == null) continue;
                        dtos.Add(ToDto(v));
                    }
                }

                string json = _serializer.Serialize(dtos);
                File.WriteAllText(_filePath, json);
            } catch {
                // 与设备仓储一致：持久化失败不向上抛，避免拖垮 UI
            }
        }

        private static VariableDto ToDto (VariableItem v) {
            return new VariableDto {
                Id = v.Id,
                DeviceId = v.DeviceId,
                Name = v.Name,
                Address = v.Address,
                DataType = (int)v.DataType,
                Access = (int)v.Access,
                Length = v.Length
            };
        }

        private static VariableItem FromDto (VariableDto d) {
            var v = new VariableItem
            {
                Id = d.Id ?? Guid.NewGuid().ToString("N"),
                DeviceId = d.DeviceId ?? "",
                Name = string.IsNullOrEmpty(d.Name) ? "新变量" : d.Name,
                Address = d.Address ?? "",
                DataType = (VariableDataType)d.DataType,
                Access = (VariableAccess)d.Access,
                Length = d.Length,
                Quality = DataQuality.Bad,
                LastError = "",
                LastValue = null
            };
            return v;
        }

        /// <summary>仅用于 JSON 序列化的平面结构。</summary>
        public class VariableDto {
            public string Id { get; set; }
            public string DeviceId { get; set; }
            public string Name { get; set; }
            public string Address { get; set; }
            public int DataType { get; set; }
            public int Access { get; set; }
            public int Length { get; set; }
        }
    }
}