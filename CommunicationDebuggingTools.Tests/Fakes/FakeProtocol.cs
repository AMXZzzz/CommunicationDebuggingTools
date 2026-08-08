using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Tests.Fakes {
    public class FakeProtocol : IProtocol {
        public bool ConnectResult { get; set; }
        public int ConnectCallCount { get; private set; }

        public string GetProtocolName () {
            return "Modbus TCP";
        }

        public bool IsConnected { get; private set; }

        public Task<bool> ConnectAsync (string ip, int port, int unitId, CancellationToken cancellationToken) {
            ConnectCallCount++;
            IsConnected = ConnectResult;
            return Task.FromResult(ConnectResult);
        }

        public void Disconnect () {
            IsConnected = false;
        }

        // 其余 IProtocol 成员：空实现或 throw NotSupportedException
        public ushort[] ReadWords (string address, int count) { return new ushort[0]; }
        public void WriteWord (string address, ushort value) { }
        public void WriteWords (string address, ushort[] values) { }
        public bool[] ReadBits (string address, int count) { return new bool[0]; }
        public void WriteBit (string address, bool value) { }
        public float ReadFloat (string address, WordOrder wordOrder) { return 0; }
        public void WriteFloat (string address, float value, WordOrder wordOrder) { }
        public string ReadString (string address, int length, Encoding encoding, ByteOrder byteOrder) {
            return "";
        }
        public void WriteString (string address, string value, int maxLength, Encoding encoding, ByteOrder byteOrder) { }
    }
}