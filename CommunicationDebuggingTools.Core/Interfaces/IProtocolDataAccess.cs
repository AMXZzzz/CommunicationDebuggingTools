using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 协议数据读写契约。
    /// 入参与出参均为 <see cref="ProtocolDataMessage"/>，Core 不解析 Address。
    /// </summary>
    public interface IProtocolDataAccess {
        /// <summary>
        /// 按报文中的地址与类型读取，回填 Value、Success、Quality、ErrorMessage。
        /// </summary>
        Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 按报文写入 Value，回填 Success、ErrorMessage。
        /// </summary>
        Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);
    }
}