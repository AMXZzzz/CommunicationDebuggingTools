using System;
using System.Net.Sockets;
using System.Text;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;

namespace Plugin.ModbusTcp {
    /// <summary>
    /// Modbus TCP 协议插件
    /// 地址示例："0"、"100"、"40001"
    /// </summary>
    public class ModbusTcpProtocol : IProtocol, IDisposable {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private byte _unitId = 1;
        private ushort _transactionId;
        private readonly object _sync = new object();
        private int _timeoutMs = 3000;
        private bool _disposed;

        public bool IsConnected {
            get { return _tcp != null && _tcp.Connected && _stream != null; }
        }

        public int TimeoutMs {
            get { return _timeoutMs; }
            set { _timeoutMs = value < 500 ? 500 : value; }
        }

        public string GetProtocolName () {
            return "Modbus TCP";
        }

        // ==================== 连接 ====================

        public bool Connect (string ip, int port, int unitId) {
            Disconnect();

            if (string.IsNullOrWhiteSpace(ip))
                return false;

            _unitId = (byte)(unitId < 0 ? 0 : (unitId > 255 ? 255 : unitId));

            try {
                _tcp = new TcpClient();
                IAsyncResult ar = _tcp.BeginConnect(ip, port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(_timeoutMs);
                if (!ok || !_tcp.Connected) {
                    Disconnect();
                    return false;
                }
                _tcp.EndConnect(ar);

                _stream = _tcp.GetStream();
                _stream.ReadTimeout = _timeoutMs;
                _stream.WriteTimeout = _timeoutMs;
                return true;
            } catch {
                Disconnect();
                return false;
            }
        }

        public void Disconnect () {
            try {
                if (_stream != null) {
                    _stream.Close();
                    _stream = null;
                }
            } catch { }

            try {
                if (_tcp != null) {
                    _tcp.Close();
                    _tcp = null;
                }
            } catch { }
        }

        // ==================== 字读写 ====================

        public ushort[] ReadWords (string address, int count) {
            int addr = ParseAddress(address);
            return ReadHoldingRegisters(addr, count);
        }

        public void WriteWord (string address, ushort value) {
            int addr = ParseAddress(address);
            WriteSingleRegister(addr, value);
        }

        public void WriteWords (string address, ushort[] values) {
            int addr = ParseAddress(address);
            WriteMultipleRegisters(addr, values);
        }

        // ==================== 位读写 ====================

        public bool[] ReadBits (string address, int count) {
            int addr = ParseAddress(address);
            return ReadCoils(addr, count);
        }

        public void WriteBit (string address, bool value) {
            int addr = ParseAddress(address);
            WriteSingleCoil(addr, value);
        }

        // ==================== 浮点 ====================

        public float ReadFloat (string address, WordOrder wordOrder) {
            ushort[] regs = ReadWords(address, 2);
            bool highFirst = wordOrder == WordOrder.HighWordFirst;
            return RegistersToFloat(regs[0], regs[1], highFirst);
        }

        public void WriteFloat (string address, float value, WordOrder wordOrder) {
            ushort high, low;
            bool highFirst = wordOrder == WordOrder.HighWordFirst;
            FloatToRegisters(value, out high, out low, highFirst);
            WriteWords(address, new ushort[] { high, low });
        }

        // ==================== 字符串 ====================

        public string ReadString (string address, int length, Encoding encoding, ByteOrder byteOrder) {
            if (encoding == null)
                encoding = Encoding.ASCII;
            if (length < 1)
                throw new ArgumentOutOfRangeException("length");

            int maxBytes = encoding.GetMaxByteCount(length);
            int regCount = (maxBytes + 1) / 2;
            if (regCount < 1)
                regCount = 1;

            ushort[] regs = ReadWords(address, regCount);
            byte[] bytes = RegistersToBytes(regs, byteOrder);

            int n = 0;
            while (n < bytes.Length && bytes[n] != 0)
                n++;

            string s = encoding.GetString(bytes, 0, n);
            if (s.Length > length)
                s = s.Substring(0, length);
            return s;
        }

        public void WriteString (string address, string value, int maxLength,
                                Encoding encoding, ByteOrder byteOrder) {
            if (encoding == null)
                encoding = Encoding.ASCII;
            if (maxLength < 1)
                throw new ArgumentOutOfRangeException("maxLength");
            if (value == null)
                value = "";

            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);

            byte[] raw = encoding.GetBytes(value);
            int maxBytes = maxLength;
            if (raw.Length > maxBytes) {
                byte[] cut = new byte[maxBytes];
                Buffer.BlockCopy(raw, 0, cut, 0, maxBytes);
                raw = cut;
            }

            int regCount = (maxBytes + 1) / 2;
            byte[] padded = new byte[regCount * 2];
            Buffer.BlockCopy(raw, 0, padded, 0, raw.Length);

            ushort[] regs = BytesToRegisters(padded, byteOrder);
            WriteWords(address, regs);
        }

        // ==================== 地址解析 ====================

        /// <summary>
        /// "0" / "100" / "40001"（保持寄存器 40001 起）
        /// </summary>
        private static int ParseAddress (string address) {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("地址为空");

            address = address.Trim().ToUpperInvariant();

            int n;
            if (address.Length >= 5 && address[0] == '4' && int.TryParse(address, out n) && n >= 40001)
                return n - 40001;

            if (int.TryParse(address, out n) && n >= 0)
                return n;

            throw new ArgumentException("无法解析的 Modbus 地址: " + address);
        }

        // ==================== FC 实现 ====================

        private ushort[] ReadHoldingRegisters (int address, int count) {
            EnsureConnected();
            if (count < 1 || count > 125)
                throw new ArgumentOutOfRangeException("count");

            byte[] pdu = new byte[5];
            pdu[0] = 0x03;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)(address & 0xFF);
            pdu[3] = (byte)(count >> 8);
            pdu[4] = (byte)(count & 0xFF);

            byte[] resp = SendAndReceive(pdu);
            CheckException(resp);

            int byteCount = resp[8];
            if (byteCount != count * 2)
                throw new Exception("返回字节数与请求不符");

            ushort[] regs = new ushort[count];
            for (int i = 0; i < count; i++) {
                int idx = 9 + i * 2;
                regs[i] = (ushort)((resp[idx] << 8) | resp[idx + 1]);
            }
            return regs;
        }

        private void WriteSingleRegister (int address, ushort value) {
            EnsureConnected();

            byte[] pdu = new byte[5];
            pdu[0] = 0x06;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)(address & 0xFF);
            pdu[3] = (byte)(value >> 8);
            pdu[4] = (byte)(value & 0xFF);

            CheckException(SendAndReceive(pdu));
        }

        private void WriteMultipleRegisters (int address, ushort[] values) {
            EnsureConnected();
            if (values == null || values.Length < 1 || values.Length > 123)
                throw new ArgumentOutOfRangeException("values");

            int byteCount = values.Length * 2;
            byte[] pdu = new byte[6 + byteCount];
            pdu[0] = 0x10;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)(address & 0xFF);
            pdu[3] = (byte)(values.Length >> 8);
            pdu[4] = (byte)(values.Length & 0xFF);
            pdu[5] = (byte)byteCount;

            for (int i = 0; i < values.Length; i++) {
                pdu[6 + i * 2] = (byte)(values[i] >> 8);
                pdu[7 + i * 2] = (byte)(values[i] & 0xFF);
            }

            CheckException(SendAndReceive(pdu));
        }

        private bool[] ReadCoils (int address, int count) {
            EnsureConnected();
            if (count < 1 || count > 2000)
                throw new ArgumentOutOfRangeException("count");

            byte[] pdu = new byte[5];
            pdu[0] = 0x01;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)(address & 0xFF);
            pdu[3] = (byte)(count >> 8);
            pdu[4] = (byte)(count & 0xFF);

            byte[] resp = SendAndReceive(pdu);
            CheckException(resp);

            bool[] coils = new bool[count];
            for (int i = 0; i < count; i++) {
                int b = resp[9 + i / 8];
                coils[i] = (b & (1 << (i % 8))) != 0;
            }
            return coils;
        }

        private void WriteSingleCoil (int address, bool value) {
            EnsureConnected();

            byte[] pdu = new byte[5];
            pdu[0] = 0x05;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)(address & 0xFF);
            pdu[3] = value ? (byte)0xFF : (byte)0x00;
            pdu[4] = 0x00;

            CheckException(SendAndReceive(pdu));
        }

        // ==================== 转换工具 ====================

        private static float RegistersToFloat (ushort high, ushort low, bool highWordFirst) {
            byte[] bytes = new byte[4];
            if (highWordFirst) {
                bytes[0] = (byte)(high >> 8);
                bytes[1] = (byte)(high & 0xFF);
                bytes[2] = (byte)(low >> 8);
                bytes[3] = (byte)(low & 0xFF);
            } else {
                bytes[0] = (byte)(low >> 8);
                bytes[1] = (byte)(low & 0xFF);
                bytes[2] = (byte)(high >> 8);
                bytes[3] = (byte)(high & 0xFF);
            }

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToSingle(bytes, 0);
        }

        private static void FloatToRegisters (float value, out ushort high, out ushort low, bool highWordFirst) {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            ushort w0 = (ushort)((bytes[0] << 8) | bytes[1]);
            ushort w1 = (ushort)((bytes[2] << 8) | bytes[3]);

            if (highWordFirst) {
                high = w0;
                low = w1;
            } else {
                high = w1;
                low = w0;
            }
        }

        private static byte[] RegistersToBytes (ushort[] regs, ByteOrder byteOrder) {
            byte[] bytes = new byte[regs.Length * 2];
            for (int i = 0; i < regs.Length; i++) {
                if (byteOrder == ByteOrder.BigEndian) {
                    bytes[i * 2] = (byte)(regs[i] >> 8);
                    bytes[i * 2 + 1] = (byte)(regs[i] & 0xFF);
                } else {
                    bytes[i * 2] = (byte)(regs[i] & 0xFF);
                    bytes[i * 2 + 1] = (byte)(regs[i] >> 8);
                }
            }
            return bytes;
        }

        private static ushort[] BytesToRegisters (byte[] bytes, ByteOrder byteOrder) {
            int regCount = (bytes.Length + 1) / 2;
            ushort[] regs = new ushort[regCount];
            for (int i = 0; i < regCount; i++) {
                int bi = i * 2;
                byte b0 = bi < bytes.Length ? bytes[bi] : (byte)0;
                byte b1 = (bi + 1) < bytes.Length ? bytes[bi + 1] : (byte)0;

                if (byteOrder == ByteOrder.BigEndian)
                    regs[i] = (ushort)((b0 << 8) | b1);
                else
                    regs[i] = (ushort)((b1 << 8) | b0);
            }
            return regs;
        }

        // ==================== 底层收发 ====================

        private void EnsureConnected () {
            if (!IsConnected)
                throw new InvalidOperationException("未连接");
        }

        private byte[] SendAndReceive (byte[] pdu) {
            lock (_sync) {
                EnsureConnected();

                _transactionId++;
                ushort tid = _transactionId;
                int len = 1 + pdu.Length;

                byte[] frame = new byte[7 + pdu.Length];
                frame[0] = (byte)(tid >> 8);
                frame[1] = (byte)(tid & 0xFF);
                frame[2] = 0x00;
                frame[3] = 0x00;
                frame[4] = (byte)(len >> 8);
                frame[5] = (byte)(len & 0xFF);
                frame[6] = _unitId;
                Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);

                _stream.Write(frame, 0, frame.Length);
                _stream.Flush();

                byte[] header = ReadExact(7);
                int pduLen = ((header[4] << 8) | header[5]) - 1;
                if (pduLen < 2)
                    throw new Exception("PDU 长度非法");

                byte[] body = ReadExact(pduLen);
                byte[] resp = new byte[7 + pduLen];
                Buffer.BlockCopy(header, 0, resp, 0, 7);
                Buffer.BlockCopy(body, 0, resp, 7, pduLen);
                return resp;
            }
        }

        private byte[] ReadExact (int size) {
            byte[] buf = new byte[size];
            int offset = 0;
            while (offset < size) {
                int n = _stream.Read(buf, offset, size - offset);
                if (n <= 0)
                    throw new Exception("连接已断开或读超时");
                offset += n;
            }
            return buf;
        }

        private static void CheckException (byte[] resp) {
            if (resp == null || resp.Length < 9)
                throw new Exception("响应无效");
            if ((resp[7] & 0x80) != 0)
                throw new Exception(string.Format("Modbus 异常码: 0x{0:X2}", resp[8]));
        }

        public void Dispose () {
            if (_disposed)
                return;
            _disposed = true;
            Disconnect();
        }
    }
}