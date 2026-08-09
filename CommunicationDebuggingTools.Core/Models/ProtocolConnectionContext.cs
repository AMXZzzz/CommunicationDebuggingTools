using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 建立协议会话时的共性连接上下文。
    /// 协议私有参数只放在 ProtocolSettingsJson，由插件解析。
    /// </summary>
    public class ProtocolConnectionContext {
        public string Ip { get; set; }

        public int Port { get; set; }

        /// <summary>协议私有连接参数 JSON 原文。</summary>
        public string ProtocolSettingsJson { get; set; }

        public ByteOrder ByteOrder { get; set; }

        public WordOrder WordOrder { get; set; }

        public StringEncodingKind StringEncoding { get; set; }

        public int TimeoutMs { get; set; }

        public ProtocolConnectionContext () {
            Ip = "";
            Port = 0;
            ProtocolSettingsJson = "{}";
            ByteOrder = ByteOrder.BigEndian;
            WordOrder = WordOrder.HighWordFirst;
            StringEncoding = StringEncodingKind.Utf8;
            TimeoutMs = 3000;
        }
    }
}