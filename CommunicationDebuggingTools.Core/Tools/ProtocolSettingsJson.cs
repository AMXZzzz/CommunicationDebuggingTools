using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace CommunicationDebuggingTools.Core.Tools {
    /// <summary>
    /// 解析设备 ProtocolSettingsJson（扁平数字字段）。
    /// 插件只取键值，不在此解释协议语义。
    /// </summary>
    public static class ProtocolSettingsJson {
        /// <summary>读取整型字段；缺失或非法时返回 defaultValue。</summary>
        public static int GetInt (string json, string key, int defaultValue) {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return defaultValue;

            try {
                var ser = new JavaScriptSerializer();
                var dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict == null || dict.Count == 0)
                    return defaultValue;

                object raw = null;
                foreach (KeyValuePair<string, object> kv in dict) {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                        raw = kv.Value;
                        break;
                    }
                }

                if (raw == null)
                    return defaultValue;

                if (raw is int)
                    return (int)raw;

                if (raw is long)
                    return (int)(long)raw;

                int v;
                if (int.TryParse(Convert.ToString(raw), out v))
                    return v;
            } catch {
                // 解析失败用默认值
            }

            return defaultValue;
        }
    }
}