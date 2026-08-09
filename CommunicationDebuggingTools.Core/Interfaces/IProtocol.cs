using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 协议会话契约。
    /// 连接使用 <see cref="ProtocolConnectionContext"/>；
    /// 读写使用 <see cref="IProtocolDataAccess"/> 与 <see cref="ProtocolDataMessage"/>。
    /// 旧的 ip/port/unitId 签名及 ReadWords 等 API 已全部废弃。
    /// </summary>
    public interface IProtocol {
        /// <summary>
        /// 协议显示名称（如 "Modbus TCP"），
        /// 须与设备 Protocol 字段、插件解析器注册名一致。
        /// </summary>
        string GetProtocolName ();

        /// <summary>当前是否已建立有效会话。</summary>
        bool IsConnected { get; }

        /// <summary>
        /// 使用共性连接上下文建立会话。
        /// 协议私有参数仅从 <see cref="ProtocolConnectionContext.ProtocolSettingsJson"/> 解析，
        /// Core/Business 不解释 JSON 内容。
        /// </summary>
        /// <param name="context">IP、端口、私有 JSON、默认序与编码等。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否连接成功。</returns>
        Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken);

        /// <summary>断开会话并释放底层资源。</summary>
        void Disconnect ();
    }
}