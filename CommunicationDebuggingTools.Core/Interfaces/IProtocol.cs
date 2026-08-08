using System.Text;
using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Interfaces {
    public interface IProtocol {
        string GetProtocolName ();
        bool IsConnected { get; }

        bool Connect (string ip, int port, int unitId);
        void Disconnect ();

        // ----- 字 -----
        ushort[] ReadWords (string address, int count);
        void WriteWord (string address, ushort value);
        void WriteWords (string address, ushort[] values);

        // ----- 位 -----
        bool[] ReadBits (string address, int count);
        void WriteBit (string address, bool value);

        // ----- 浮点：字序可选 -----
        float ReadFloat (string address, WordOrder wordOrder);
        void WriteFloat (string address, float value, WordOrder wordOrder);

        // ----- 字符串：编码 + 寄存器内字节序 -----
        /// <param name="address">起始地址</param>
        /// <param name="length">最大字符数（或按协议约定为字节数）</param>
        /// <param name="encoding">如 Encoding.ASCII、UTF8、Default(GB2312)</param>
        /// <param name="byteOrder">每个寄存器内字节序</param>
        string ReadString (string address, int length, Encoding encoding, ByteOrder byteOrder);

        void WriteString (string address, string value, int maxLength, Encoding encoding, ByteOrder byteOrder);
    }
}