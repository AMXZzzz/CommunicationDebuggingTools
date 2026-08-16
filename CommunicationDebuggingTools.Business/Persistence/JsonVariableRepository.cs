using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Business.Persistence {
    /// <summary>
    /// 变量配置 JSON 仓储。version=1：{ "version":1, "variables":[ ... ] }。
    /// 兼容旧版根数组；LastValue/Quality/LastError 不落盘。
    /// </summary>
    public class JsonVariableRepository : IVariableRepository {
        public const int CurrentVersion = 1;

        private readonly string _filePath;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public JsonVariableRepository (string filePath = null) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                _filePath = Path.Combine(dir, "config", "variables.json");
            } else {
                _filePath = filePath;
            }
        }

        public IList<VariableItem> LoadAll () {
            if (!File.Exists(_filePath))
                return new List<VariableItem>();

            string json;
            try {
                json = File.ReadAllText(_filePath);
            } catch (Exception ex) {
                Trace.TraceWarning("读取变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            }

            if (string.IsNullOrWhiteSpace(json))
                return new List<VariableItem>();

            try {
                return ParseDocument(json);
            } catch (Exception ex) {
                Trace.TraceWarning("解析变量配置失败: {0}", ex.Message);
                return new List<VariableItem>();
            }
        }

        public void SaveAll (IList<VariableItem> items) {
            if (items == null)
                items = new List<VariableItem>();

            string dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var dtos = new List<VariableDto>();
            foreach (VariableItem v in items) {
                if (v == null) continue;
                dtos.Add(ToDto(v));
            }

            var doc = new VariableFileDocument {
                version = CurrentVersion,
                variables = dtos
            };
            string json = _serializer.Serialize(doc);
            string tempPath = _filePath + ".tmp";
            try {
                File.WriteAllText(tempPath, json);
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(tempPath, _filePath);
            } catch (Exception ex) {
                Trace.TraceError("保存变量配置失败: {0}", ex.Message);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private IList<VariableItem> ParseDocument (string json) {
            object root = _serializer.DeserializeObject(json);
            if (root == null)
                return new List<VariableItem>();

            if (root is object[] arr)
                return MapArray(arr);

            if (root is Dictionary<string, object> map) {
                object varsObj;
                if (!map.TryGetValue("variables", out varsObj) || varsObj == null)
                    return new List<VariableItem>();
                if (varsObj is object[] varsArr)
                    return MapArray(varsArr);
                if (varsObj is ArrayList list) {
                    var tmp = new object[list.Count];
                    list.CopyTo(tmp);
                    return MapArray(tmp);
                }
            }
            return new List<VariableItem>();
        }

        private IList<VariableItem> MapArray (object[] arr) {
            var result = new List<VariableItem>();
            if (arr == null) return result;
            foreach (object item in arr) {
                VariableItem v = MapVariable(item as Dictionary<string, object>);
                if (v != null) result.Add(v);
            }
            return result;
        }

        private static VariableItem MapVariable (Dictionary<string, object> m) {
            if (m == null) return null;
            var v = new VariableItem {
                Id = GetString(m, "Id") ?? Guid.NewGuid().ToString("N"),
                DeviceId = GetString(m, "DeviceId") ?? "",
                Name = string.IsNullOrEmpty(GetString(m, "Name")) ? "新变量" : GetString(m, "Name"),
                Address = GetString(m, "Address") ?? "",
                DataType = (VariableDataType)GetInt(m, "DataType", (int)VariableDataType.Int16),
                Access = (VariableAccess)GetInt(m, "Access", (int)VariableAccess.ReadWrite),
                Length = GetInt(m, "Length", 0),
                Unit = GetString(m, "Unit") ?? "",
                Category = string.IsNullOrEmpty(GetString(m, "Category")) ? "状态点" : GetString(m, "Category"),
                Description = GetString(m, "Description") ?? "",
                Quality = DataQuality.Bad,
                LastError = "",
                LastValue = null
            };
            int scan;
            if (TryGetInt(m, "ScanRateMs", out scan) && scan > 0)
                v.ScanRateMs = scan;
            bool poll;
            if (TryGetBool(m, "IsPollingEnabled", out poll))
                v.IsPollingEnabled = poll;
            return v;
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
                Description = v.Description ?? "",
                ScanRateMs = v.ScanRateMs,
                IsPollingEnabled = v.IsPollingEnabled
            };
        }

        private static string GetString (Dictionary<string, object> m, string key) {
            object o;
            if (!m.TryGetValue(key, out o) || o == null) return null;
            return Convert.ToString(o);
        }

        private static int GetInt (Dictionary<string, object> m, string key, int def) {
            int v;
            return TryGetInt(m, key, out v) ? v : def;
        }

        private static bool TryGetInt (Dictionary<string, object> m, string key, out int value) {
            value = 0;
            object o;
            if (!m.TryGetValue(key, out o) || o == null) return false;
            try { value = Convert.ToInt32(o); return true; } catch { return false; }
        }

        private static bool TryGetBool (Dictionary<string, object> m, string key, out bool value) {
            value = false;
            object o;
            if (!m.TryGetValue(key, out o) || o == null) return false;
            try { value = Convert.ToBoolean(o); return true; } catch {
                try {
                    int i = Convert.ToInt32(o);
                    value = i != 0;
                    return true;
                } catch { return false; }
            }
        }

        public class VariableFileDocument {
            public int version { get; set; }
            public List<VariableDto> variables { get; set; }
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
            public int ScanRateMs { get; set; }
            public bool IsPollingEnabled { get; set; }
        }
    }
}
