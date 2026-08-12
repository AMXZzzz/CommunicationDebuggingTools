using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 建立协议会话时的共性连接上下文。
    /// 协议私有参数只放在 ProtocolSettingsJson，由插件解析。
    /// </summary>
    public class ProtocolConnectionContext {
        //! IP 地址，IPv4 或 IPv6。
        public string Ip { get; set; }
        //! 端口
        public int Port { get; set; }

        /// <summary>
        /// 协议私有连接参数 以JSON透传递 
        /// </summary>
        public string ProtocolSettingsJson { get; set; }

        //! 字节序
        public ByteOrder ByteOrder { get; set; }

        //! 字顺序
        public WordOrder WordOrder { get; set; }

        //! 字符串编码
        public StringEncodingKind StringEncoding { get; set; }

        //! 连接超时，单位毫秒
        public int TimeoutMs { get; set; }

        //! 构造函数，初始化默认值
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