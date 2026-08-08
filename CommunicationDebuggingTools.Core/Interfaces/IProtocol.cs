using CommunicationDebuggingTools.Core.Enums;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 通信协议统一契约，每个具体协议（如 Modbus TCP、S7 等）均实现本接口。
    /// 业务层/UI 层只面向本接口编程，不关心底层通信细节，方便插件化拓展新协议。
    /// </summary>
    public interface IProtocol {
        /// <summary>获取协议显示名称（如 "Modbus TCP"），需与插件解析器中注册的名称一致。</summary>
        string GetProtocolName ();

        /// <summary>当前是否已建立有效连接。</summary>
        bool IsConnected { get; }

        /// <summary>
        /// 建立与设备的通信连接。
        /// </summary>
        /// <param name="ip">设备 IP 地址。</param>
        /// <param name="port">通信端口。</param>
        /// <param name="unitId">站号 / Unit ID（Modbus 为从站地址）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接是否成功。</returns>
        Task<bool> ConnectAsync (string ip, int port, int unitId, CancellationToken cancellationToken);

        /// <summary>断开当前连接并释放相关资源。</summary>
        void Disconnect ();

        // ----- 字（16 位寄存器）-----

        /// <summary>从指定地址连续读取多个 16 位字。</summary>
        /// <param name="address">起始寄存器地址（具体格式由协议实现约定）。</param>
        /// <param name="count">读取的字数。</param>
        ushort[] ReadWords (string address, int count);

        /// <summary>向指定地址写入单个 16 位字。</summary>
        void WriteWord (string address, ushort value);

        /// <summary>向指定地址连续写入多个 16 位字。</summary>
        void WriteWords (string address, ushort[] values);

        // ----- 位（布尔线圈）-----

        /// <summary>从指定地址连续读取多个位。</summary>
        bool[] ReadBits (string address, int count);

        /// <summary>向指定地址写入单个位。</summary>
        void WriteBit (string address, bool value);

        // ----- 浮点：跨寄存器，字序可选 -----

        /// <summary>读取单精度浮点数（占用两个字）。</summary>
        /// <param name="address">起始地址。</param>
        /// <param name="wordOrder">两个字之间的高低顺序。</param>
        float ReadFloat (string address, WordOrder wordOrder);

        /// <summary>写入单精度浮点数。</summary>
        void WriteFloat (string address, float value, WordOrder wordOrder);

        // ----- 字符串：编码 + 寄存器内字节序 -----

        /// <summary>
        /// 从寄存器中读取字符串（自动去除末尾空字节）。
        /// </summary>
        /// <param name="address">起始地址。</param>
        /// <param name="length">最大字符数（或按协议约定为字节数）。</param>
        /// <param name="encoding">字符串编码，如 Encoding.ASCII、UTF8、Default（GB2312）。</param>
        /// <param name="byteOrder">每个寄存器内部两字节的高低顺序。</param>
        string ReadString (string address, int length, Encoding encoding, ByteOrder byteOrder);

        /// <summary>向寄存器写入字符串，超过 <paramref name="maxLength"/> 部分会被截断。</summary>
        void WriteString (string address, string value, int maxLength, Encoding encoding, ByteOrder byteOrder);
    }
}