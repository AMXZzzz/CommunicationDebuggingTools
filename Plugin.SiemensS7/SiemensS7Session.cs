using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.SiemensS7 {
    /// <summary>
    /// S7 会话：解析连接 JSON、解析地址、持有 TCP。
    /// 完整 ISO-on-TCP / S7 读写 PDU 可在本类后续补全。
    /// </summary>
    internal sealed class SiemensS7Session : IDisposable {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private int _timeoutMs = 3000;
        private bool _disposed;

        public int Rack { get; private set; }
        public int Slot { get; private set; } = 1;

        public bool IsConnected =>
            _tcp != null && _tcp.Connected && _stream != null;

        public int TimeoutMs {
            get => _timeoutMs;
            set => _timeoutMs = value < 500 ? 500 : value;
        }

        /// <summary>从 ProtocolSettingsJson 读取 rack/slot。</summary>
        public void ApplySettingsJson (string json) {
            Rack = ReadIntField(json, "rack", 0);
            Slot = ReadIntField(json, "slot", 1);
            if (Rack < 0) Rack = 0;
            if (Slot < 0) Slot = 0;
        }

        /// <summary>异步连接 PLC（默认端口 102）。</summary>
        public async Task ConnectAsync (string ip, int port, int timeoutMs, CancellationToken ct) {
            Disconnect();
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("IP 为空");

            if (port <= 0)
                port = 102;

            TimeoutMs = timeoutMs > 0 ? timeoutMs : 3000;
            _tcp = new TcpClient();

            var connectTask = _tcp.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(TimeoutMs, ct);
            if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) {
                Disconnect();
                throw new TimeoutException("连接超时");
            }
            await connectTask;

            if (!_tcp.Connected || ct.IsCancellationRequested) {
                Disconnect();
                throw new InvalidOperationException("连接失败或已取消");
            }

            _stream = _tcp.GetStream();
            _stream.ReadTimeout = TimeoutMs;
            _stream.WriteTimeout = TimeoutMs;

            // TODO: COTP + S7 Setup Communication（后续接入）
        }

        public void Disconnect () {
            try { if (_stream != null) { _stream.Close(); _stream = null; } } catch { }
            try { if (_tcp != null) { _tcp.Close(); _tcp = null; } } catch { }
        }

        /// <summary>
        /// 解析 S7 地址。支持示例：
        /// DB1.DBX0.0 / DB1.DBB0 / DB1.DBW0 / DB1.DBD0 /
        /// M0.0 / MB0 / MW0 / MD0 / I0.0 / Q0.0
        /// </summary>
        public static S7Address ParseAddress (string address) {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("地址为空");

            string a = address.Trim().ToUpperInvariant().Replace(" ", "");

            // DBn.DBXbyte.bit / DBB / DBW / DBD
            if (a.StartsWith("DB", StringComparison.Ordinal))
                return ParseDbAddress(a);

            // 过程映像 / 标志
            if (a[0] == 'M' || a[0] == 'I' || a[0] == 'Q' || a[0] == 'E' || a[0] == 'A')
                return ParseSimpleArea(a);

            throw new ArgumentException("无法解析的 S7 地址: " + address);
        }

        private static S7Address ParseDbAddress (string a) {
            // DB1.DBX0.0
            int dot1 = a.IndexOf('.');
            if (dot1 < 3)
                throw new ArgumentException("DB 地址格式错误: " + a);

            int dbNumber;
            if (!int.TryParse(a.Substring(2, dot1 - 2), out dbNumber) || dbNumber < 1)
                throw new ArgumentException("DB 号无效: " + a);

            string rest = a.Substring(dot1 + 1); // DBX0.0 / DBW4 / DBD8

            if (rest.StartsWith("DBX", StringComparison.Ordinal)) {
                string[] parts = rest.Substring(3).Split('.');
                if (parts.Length != 2)
                    throw new ArgumentException("位地址需为 DBn.DBXbyte.bit");
                int b, bit;
                if (!int.TryParse(parts[0], out b) || !int.TryParse(parts[1], out bit))
                    throw new ArgumentException("位地址数字无效");
                if (bit < 0 || bit > 7)
                    throw new ArgumentException("位号须为 0–7");
                return S7Address.DbBit(dbNumber, b, bit);
            }
            if (rest.StartsWith("DBB", StringComparison.Ordinal))
                return S7Address.DbByte(dbNumber, ParseOffset(rest.Substring(3)));
            if (rest.StartsWith("DBW", StringComparison.Ordinal))
                return S7Address.DbWord(dbNumber, ParseOffset(rest.Substring(3)));
            if (rest.StartsWith("DBD", StringComparison.Ordinal))
                return S7Address.DbDWord(dbNumber, ParseOffset(rest.Substring(3)));

            throw new ArgumentException("不支持的 DB 子类型: " + a);
        }

        private static S7Address ParseSimpleArea (string a) {
            // 统一：E→I，A→Q（德文习惯）
            char area = a[0];
            if (area == 'E') area = 'I';
            if (area == 'A') area = 'Q';

            string body = a.Substring(1);
            // M0.0 / I1.2 位
            if (body.Contains(".")) {
                string[] parts = body.Split('.');
                int b, bit;
                if (!int.TryParse(parts[0], out b) || !int.TryParse(parts[1], out bit))
                    throw new ArgumentException("区位地址无效: " + a);
                if (bit < 0 || bit > 7)
                    throw new ArgumentException("位号须为 0–7");
                return S7Address.AreaBit(area, b, bit);
            }

            // MB0 / MW2 / MD4 或裸 M0 当字节
            if (body.StartsWith("B", StringComparison.Ordinal))
                return S7Address.AreaByte(area, ParseOffset(body.Substring(1)));
            if (body.StartsWith("W", StringComparison.Ordinal))
                return S7Address.AreaWord(area, ParseOffset(body.Substring(1)));
            if (body.StartsWith("D", StringComparison.Ordinal))
                return S7Address.AreaDWord(area, ParseOffset(body.Substring(1)));

            return S7Address.AreaByte(area, ParseOffset(body));
        }

        private static int ParseOffset (string s) {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0)
                throw new ArgumentException("偏移无效: " + s);
            return v;
        }

        private static int ReadIntField (string json, string key, int defaultValue) {
            if (string.IsNullOrWhiteSpace(json))
                return defaultValue;
            try {
                int i = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return defaultValue;
                int colon = json.IndexOf(':', i);
                if (colon < 0) return defaultValue;
                int start = colon + 1;
                while (start < json.Length && (json[start] == ' ' || json[start] == '\"'))
                    start++;
                int end = start;
                bool neg = end < json.Length && json[end] == '-';
                if (neg) end++;
                while (end < json.Length && char.IsDigit(json[end]))
                    end++;
                int v;
                if (int.TryParse(json.Substring(start, end - start), out v))
                    return v;
            } catch { }
            return defaultValue;
        }

        public void Dispose () {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }

    /// <summary>S7 地址解析结果（仅插件内部）。</summary>
    internal struct S7Address {
        public char Area;      // D=DB, M, I, Q
        public int DbNumber;   // 仅 Area=D
        public int ByteOffset;
        public int Bit;        // 0–7，非位为 -1
        public S7TransportSize Size;

        public static S7Address DbBit (int db, int b, int bit) =>
            new S7Address { Area = 'D', DbNumber = db, ByteOffset = b, Bit = bit, Size = S7TransportSize.Bit };

        public static S7Address DbByte (int db, int b) =>
            new S7Address { Area = 'D', DbNumber = db, ByteOffset = b, Bit = -1, Size = S7TransportSize.Byte };

        public static S7Address DbWord (int db, int b) =>
            new S7Address { Area = 'D', DbNumber = db, ByteOffset = b, Bit = -1, Size = S7TransportSize.Word };

        public static S7Address DbDWord (int db, int b) =>
            new S7Address { Area = 'D', DbNumber = db, ByteOffset = b, Bit = -1, Size = S7TransportSize.DWord };

        public static S7Address AreaBit (char area, int b, int bit) =>
            new S7Address { Area = area, ByteOffset = b, Bit = bit, Size = S7TransportSize.Bit };

        public static S7Address AreaByte (char area, int b) =>
            new S7Address { Area = area, ByteOffset = b, Bit = -1, Size = S7TransportSize.Byte };

        public static S7Address AreaWord (char area, int b) =>
            new S7Address { Area = area, ByteOffset = b, Bit = -1, Size = S7TransportSize.Word };

        public static S7Address AreaDWord (char area, int b) =>
            new S7Address { Area = area, ByteOffset = b, Bit = -1, Size = S7TransportSize.DWord };
    }

    internal enum S7TransportSize {
        Bit,
        Byte,
        Word,
        DWord
    }
}