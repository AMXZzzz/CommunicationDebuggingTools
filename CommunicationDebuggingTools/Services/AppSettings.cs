using System;
using System.IO;
using System.Text.Json;

namespace CommunicationDebuggingTools.Services {

    /// <summary>
    /// 应用设置：运行模式 + EngineHost 地址。
    /// 持久化到 %AppData%/CommunicationDebuggingTools/settings.json。
    /// </summary>
    public sealed class AppSettings {

        // ── 默认值 ──────────────────────────────────
        public const string DefaultHostAddress = "http://127.0.0.1:5100";

        // ── 属性 ────────────────────────────────────
        /// <summary>true = 连接远端 EngineHost；false = 本地 Business 层直连。</summary>
        public bool RemoteMode { get; set; } = false;

        /// <summary>EngineHost gRPC 地址。</summary>
        public string HostAddress { get; set; } = DefaultHostAddress;

        // ── 持久化 ───────────────────────────────────
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommunicationDebuggingTools", "settings.json");

        public static AppSettings Load () {
            try {
                if (File.Exists(_path)) {
                    string json = File.ReadAllText(_path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            } catch { }
            return new AppSettings();
        }

        public void Save () {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }
    }
}
