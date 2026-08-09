using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.ModbusTcp {
    /// <summary>
    /// Modbus TCP 协议插件的默认实现（<see cref="IProtocol"/>）。
    /// 通过 TCP Socket 直接实现 Modbus 应用协议（MBAP 报文头 + 功能码 PDU），
    /// 支持保持寄存器（字）、线圈（位）、IEEE754 浮点数以及字符串的读写。
    /// 地址格式示例："0"、"100"（0 基地址）、"40001"（4xxxx 保持寄存器传统地址，自动换算为 0 基地址）。
    /// </summary>
    public class ModbusTcpProtocol : IProtocol, IDisposable {
        /// <summary>底层 TCP 客户端。</summary>
        private TcpClient _tcp;
        /// <summary>TCP 连接对应的网络数据流，用于收发 Modbus 报文。</summary>
        private NetworkStream _stream;
        /// <summary>Modbus 从站地址（Unit Id / Slave Id）。</summary>
        private byte _unitId = 1;
        /// <summary>MBAP 报文头中的事务标识符，每次请求自增，用于匹配请求与响应。</summary>
        private ushort _transactionId;
        /// <summary>同步锁，保证同一时刻只有一个线程在收发报文，避免报文交叉导致解析错乱。</summary>
        private readonly object _sync = new object();
        /// <summary>读写超时时间（毫秒），最小值为 500ms。</summary>
        private int _timeoutMs = 3000;
        /// <summary>是否已释放资源，防止重复 Dispose。</summary>
        private bool _disposed;

        /// <summary>当前是否处于已连接状态（底层 TCP 连接存在且处于 Connected 状态）。</summary>
        public bool IsConnected {
            get { return _tcp != null && _tcp.Connected && _stream != null; }
        }

        /// <summary>读写超时时间（毫秒）。设置的值小于 500ms 时会自动纠正为 500ms，避免超时过短导致误判断线。</summary>
        public int TimeoutMs {
            get { return _timeoutMs; }
            set { _timeoutMs = value < 500 ? 500 : value; }
        }

        /// <summary>返回本协议在 UI/配置中显示与匹配使用的名称。</summary>
        public string GetProtocolName () {
            return "Modbus TCP";
        }

        // ==================== 连接 ====================
        /// <summary>
        /// 使用共性连接上下文建立 Modbus TCP 会话。
        /// unitId 只从 <paramref name="context"/>.ProtocolSettingsJson 解析，不使用其它入口。
        /// </summary>
        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null)
                throw new ArgumentNullException("context");

            Disconnect();

            if (string.IsNullOrWhiteSpace(context.Ip))
                return false;

            // 协议私有参数：仅本插件理解 unitId
            _unitId = (byte)ParseUnitId(context.ProtocolSettingsJson);
            _timeoutMs = context.TimeoutMs > 0 ? context.TimeoutMs : 3000;

            try {
                _tcp = new TcpClient();

                Task connectTask = _tcp.ConnectAsync(context.Ip, context.Port);
                Task timeoutTask = Task.Delay(_timeoutMs, cancellationToken);

                Task finished = await Task.WhenAny(connectTask, timeoutTask);
                if (finished != connectTask) {
                    // 超时或取消
                    Disconnect();
                    return false;
                }

                await connectTask;

                if (!_tcp.Connected || cancellationToken.IsCancellationRequested) {
                    Disconnect();
                    return false;
                }

                _stream = _tcp.GetStream();
                _stream.ReadTimeout = _timeoutMs;
                _stream.WriteTimeout = _timeoutMs;
                return true;
            } catch {
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// 从 ProtocolSettingsJson 读取 unitId。
        /// 合法范围限制在 0–255；缺失或解析失败时默认 1。
        /// 仅 Modbus 插件使用，Core 不解析 JSON。
        /// </summary>
        private static int ParseUnitId (string protocolSettingsJson) {
            if (string.IsNullOrWhiteSpace(protocolSettingsJson))
                return 1;

            try {
                int i = protocolSettingsJson.IndexOf("unitId", StringComparison.OrdinalIgnoreCase);
                if (i < 0)
                    return 1;

                int colon = protocolSettingsJson.IndexOf(':', i);
                if (colon < 0)
                    return 1;

                int start = colon + 1;
                while (start < protocolSettingsJson.Length &&
                       (protocolSettingsJson[start] == ' ' || protocolSettingsJson[start] == '\"'))
                    start++;

                int end = start;
                while (end < protocolSettingsJson.Length && char.IsDigit(protocolSettingsJson[end]))
                    end++;

                int v;
                if (int.TryParse(protocolSettingsJson.Substring(start, end - start), out v)) {
                    if (v < 0) return 0;
                    if (v > 255) return 255;
                    return v;
                }
            } catch {
                // 忽略，走默认
            }

            return 1;
        }

        /// <summary>
        /// 断开连接并释放网络资源。内部吞掉关闭过程中的异常，保证多次调用/异常状态下调用都是安全的。
        /// </summary>
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

        // ==================== 字读写（功能码 0x03 / 0x06 / 0x10） ====================

        /// <summary>批量读取保持寄存器（功能码 0x03）。</summary>
        /// <param name="address">起始地址（支持 0 基或 4xxxx 传统地址）。</param>
        /// <param name="count">读取的寄存器数量（1~125）。</param>
        public ushort[] ReadWords (string address, int count) {
            int addr = ParseAddress(address);
            return ReadHoldingRegisters(addr, count);
        }

        /// <summary>写单个保持寄存器（功能码 0x06）。</summary>
        public void WriteWord (string address, ushort value) {
            int addr = ParseAddress(address);
            WriteSingleRegister(addr, value);
        }

        /// <summary>写多个连续保持寄存器（功能码 0x10）。</summary>
        public void WriteWords (string address, ushort[] values) {
            int addr = ParseAddress(address);
            WriteMultipleRegisters(addr, values);
        }

        // ==================== 位读写（功能码 0x01 / 0x05） ====================

        /// <summary>批量读取线圈状态（功能码 0x01）。</summary>
        public bool[] ReadBits (string address, int count) {
            int addr = ParseAddress(address);
            return ReadCoils(addr, count);
        }

        /// <summary>写单个线圈（功能码 0x05）。</summary>
        public void WriteBit (string address, bool value) {
            int addr = ParseAddress(address);
            WriteSingleCoil(addr, value);
        }

        // ==================== 浮点（占用 2 个连续寄存器，IEEE754 单精度） ====================

        /// <summary>读取一个 32 位浮点数（占用 2 个连续寄存器）。</summary>
        /// <param name="address">起始地址。</param>
        /// <param name="wordOrder">高低字序：决定两个寄存器谁存高位、谁存低位。</param>
        public float ReadFloat (string address, WordOrder wordOrder) {
            ushort[] regs = ReadWords(address, 2);
            bool highFirst = wordOrder == WordOrder.HighWordFirst;
            return RegistersToFloat(regs[0], regs[1], highFirst);
        }

        /// <summary>写入一个 32 位浮点数（占用 2 个连续寄存器）。</summary>
        public void WriteFloat (string address, float value, WordOrder wordOrder) {
            ushort high, low;
            bool highFirst = wordOrder == WordOrder.HighWordFirst;
            FloatToRegisters(value, out high, out low, highFirst);
            WriteWords(address, new ushort[] { high, low });
        }

        // ==================== 字符串（编码 + 寄存器内字节序可配置） ====================

        /// <summary>
        /// 读取字符串：按编码计算所需寄存器数量，读取后转换为字节流，
        /// 以首个 \0 字节作为字符串结束标记（截断），再按编码解码。
        /// </summary>
        /// <param name="address">起始地址。</param>
        /// <param name="length">最大字符数。</param>
        /// <param name="encoding">字符编码，为 null 时默认使用 ASCII。</param>
        /// <param name="byteOrder">每个寄存器内部的字节序。</param>
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

        /// <summary>
        /// 写入字符串：按编码转换为字节并截断到最大长度，不足部分用 0 填充满整数个寄存器后写入。
        /// </summary>
        /// <param name="address">起始地址。</param>
        /// <param name="value">待写入的字符串；为 null 时按空字符串处理。</param>
        /// <param name="maxLength">允许写入的最大字节数。</param>
        /// <param name="encoding">字符编码，为 null 时默认使用 ASCII。</param>
        /// <param name="byteOrder">每个寄存器内部的字节序。</param>
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
        /// 解析地址字符串为 0 基寄存器/线圈地址。
        /// 支持两种格式："0"/"100" 这样的原生 0 基地址；"40001" 这样的传统 4xxxx 保持寄存器地址（自动减去 40001）。
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

        // ==================== FC 实现（各功能码对应的 PDU 组装与响应解析）====================

        /// <summary>读保持寄存器（功能码 0x03），组装 PDU 并解析响应中的寄存器数组。</summary>
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

        /// <summary>写单个寄存器（功能码 0x06）。</summary>
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

        /// <summary>写多个连续寄存器（功能码 0x10）。</summary>
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

        /// <summary>读线圈状态（功能码 0x01），将响应字节按位展开为布尔数组。</summary>
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

        /// <summary>写单个线圈（功能码 0x05），true 对应 0xFF00，false 对应 0x0000。</summary>
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

        // ==================== 转换工具（寄存器 <-> 浮点数 / 字节数组）====================

        /// <summary>将两个 16 位寄存器按指定字序组合并解释为 IEEE754 单精度浮点数。</summary>
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

        /// <summary>将 IEEE754 单精度浮点数拆分为两个 16 位寄存器（按指定字序输出）。</summary>
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

        /// <summary>将寄存器数组按指定字节序展开为字节数组，供字符串解码使用。</summary>
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

        /// <summary>将字节数组按指定字节序打包为寄存器数组，供字符串写入使用；长度为奇数时末尾补 0。</summary>
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

        // ==================== 底层收发（MBAP 报文头组装/解析）====================

        /// <summary>确保当前处于已连接状态，否则抛出异常，供各读写方法在通信前统一校验。</summary>
        private void EnsureConnected () {
            if (!IsConnected)
                throw new InvalidOperationException("未连接");
        }

        /// <summary>
        /// 发送一个 PDU 并同步等待、读取完整响应。
        /// 内部会加锁保证同一时刻只有一次收发在进行，先组装 MBAP 报文头（事务标识、协议标识、长度、单元标识）+ PDU 发送，
        /// 再先读取固定 7 字节的报文头解析出后续 PDU 长度，然后按长度精确读取响应体。
        /// </summary>
        /// <param name="pdu">功能码 + 数据组成的协议数据单元。</param>
        /// <returns>完整响应报文（含 MBAP 头）。</returns>
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

        /// <summary>从网络流中精确读取指定字节数，读到 0 字节（连接断开）或超时异常时抛出错误。</summary>
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

        /// <summary>检查响应报文是否携带 Modbus 异常标志位（功能码最高位为 1），若是则抛出携带异常码的错误。</summary>
        private static void CheckException (byte[] resp) {
            if (resp == null || resp.Length < 9)
                throw new Exception("响应无效");
            if ((resp[7] & 0x80) != 0)
                throw new Exception(string.Format("Modbus 异常码: 0x{0:X2}", resp[8]));
        }

        /// <summary>释放资源：断开连接并标记为已释放，避免重复释放。</summary>
        public void Dispose () {
            if (_disposed)
                return;
            _disposed = true;
            Disconnect();
        }
    }
}