using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            if (!File.Exists(_filePath))
                return new List<VariableItem>();

            string json;
            try {
                json = File.ReadAllText(_filePath);
            } catch (IOException ex) {
                Trace.TraceError("读取变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            } catch (UnauthorizedAccessException ex) {
                Trace.TraceError("读取变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            }

            if (string.IsNullOrWhiteSpace(json))
                return new List<VariableItem>();

            try {
                List<VariableDto> dtos = _serializer.Deserialize<List<VariableDto>>(json);
                if (dtos == null)
                    return new List<VariableItem>();

                var list = new List<VariableItem>();
                foreach (VariableDto d in dtos) {
                    if (d == null) continue;
                    list.Add(FromDto(d));
                }
                return list;
            } catch (InvalidOperationException ex) {
                Trace.TraceError("解析变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            } catch (ArgumentException ex) {
                Trace.TraceError("解析变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            }
        }

        /// <summary>整体覆盖写入。</summary>
        public void SaveAll (IList<VariableItem> items) {
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
            string tempPath = _filePath + ".tmp";

            try {
                File.WriteAllText(tempPath, json);
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(tempPath, _filePath);
            } catch (IOException ex) {
                Trace.TraceError("保存变量配置失败: {0}", ex.Message);
            } catch (UnauthorizedAccessException ex) {
                Trace.TraceError("保存变量配置失败: {0}", ex.Message);
            } finally {
                if (File.Exists(tempPath)) {
                    try {
                        File.Delete(tempPath);
                    } catch (IOException ex) {
                        Trace.TraceError("清理变量配置临时文件失败: {0}", ex.Message);
                    } catch (UnauthorizedAccessException ex) {
                        Trace.TraceError("清理变量配置临时文件失败: {0}", ex.Message);
                    }
                }
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
                Length = v.Length,
                Unit = v.Unit ?? "",
                Category = v.Category ?? "",
                Description = v.Description ?? ""
            };
        }

        private static VariableItem FromDto (VariableDto d) {
            return new VariableItem {
                Id = d.Id ?? Guid.NewGuid().ToString("N"),
                DeviceId = d.DeviceId ?? "",
                Name = string.IsNullOrEmpty(d.Name) ? "新变量" : d.Name,
                Address = d.Address ?? "",
                DataType = (VariableDataType)d.DataType,
                Access = (VariableAccess)d.Access,
                Length = d.Length,
                Unit = d.Unit ?? "",
                Category = string.IsNullOrEmpty(d.Category) ? "状态点" : d.Category,
                Description = d.Description ?? "",
                Quality = DataQuality.Bad,
                LastError = "",
                LastValue = null
            };
        }

        public class VariableDto {
            public string Id { get; set; }
            public string DeviceId { get; set; }
            public string Name { get; set; }
            public string Address { get; set; }
            public int DataType { get; set; }
            public int Access { get; set; }
            public int Length { get; set; }
            public string Unit { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
        }
    }
}