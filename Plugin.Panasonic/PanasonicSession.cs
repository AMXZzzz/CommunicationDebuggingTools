using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Panasonic {
    /// <summary>
    /// 松下 MEWTOCOL-COM 会话：站号、地址解析、TCP、报文收发。
    /// 帧格式：% + 站号(2位十六进制) + # + 命令 + BCC(2位十六进制) + CR
    /// BCC：对「站号+#命令」整段逐字节异或。
    /// </summary>
    internal sealed class PanasonicSession : IDisposable {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private int _timeoutMs = 3000;
        private bool _disposed;
        private readonly object _sync = new object();

        /// <summary>MEWTOCOL 站号（1–99）。</summary>
        public int Station { get; private set; } = 1;

        /// <summary>
        /// 主动探测 TCP 连接是否仍然存活。
        /// 不能用 TcpClient.Connected —— 它是缓存属性，对端关闭后不会自动变 false。
        /// Socket.Poll(SelectRead) + Available==0 是检测对端关闭的标准手段：
        ///   Poll 返回 true  且 Available == 0  → 对端已关闭（收到 TCP FIN/RST）
        ///   Poll 返回 true  且 Available  > 0  → 有数据可读，连接正常
        ///   Poll 返回 false               → 暂无事件，连接正常
        /// </summary>
        public bool IsConnected {
            get {
                if (_tcp == null || _stream == null) return false;
                System.Net.Sockets.Socket s = _tcp.Client;
                if (s == null || !s.Connected) return false;
                try {
                    return !(s.Poll(0, System.Net.Sockets.SelectMode.SelectRead)
                             && s.Available == 0);
                } catch {
                    return false;
                }
            }
        }

        public int TimeoutMs {
            get => _timeoutMs;
            set => _timeoutMs = value < 500 ? 500 : value;
        }

        public void ApplySettingsJson (string json) {
            Station = ReadIntField(json, "station", 1);
            if (Station < 0) Station = 0;
            if (Station > 99) Station = 99;
        }

        public async Task ConnectAsync (string ip, int port, int timeoutMs, CancellationToken ct) {
            Disconnect();
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("IP 为空");

            if (port <= 0)
                port = 9094;

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
        }

        public void Disconnect () {
            try { if (_stream != null) { _stream.Close(); _stream = null; } } catch { }
            try { if (_tcp != null) { _tcp.Close(); _tcp = null; } } catch { }
        }

        // -------------------- 报文 --------------------

        /// <summary>
        /// 发送命令正文（不含站号/#/%/BCC），返回 PLC 响应 ASCII（到 CR）。
        /// 例：commandBody = "WCSR001001"
        /// </summary>
        public string Transact (string commandBody) {
            if (string.IsNullOrEmpty(commandBody))
                throw new ArgumentException("命令为空");
            if (!IsConnected)
                throw new InvalidOperationException("未连接");

            // payload = SS + # + CMD
            string payload = Station.ToString("X2") + "#" + commandBody;
            string frame = "%" + payload + CalcBcc(payload) + "\r";
            byte[] send = Encoding.ASCII.GetBytes(frame);

            lock (_sync) {
                _stream.Write(send, 0, send.Length);
                _stream.Flush();
                return ReadLineCr();
            }
        }

        /// <summary>读到 CR 为止（不含 CR）。</summary>
        private string ReadLineCr () {
            var sb = new StringBuilder(64);
            var buf = new byte[1];
            int guard = 0;
            while (guard++ < 4096) {
                int n = _stream.Read(buf, 0, 1);
                if (n <= 0)
                    throw new Exception("连接已断开或读超时");
                char c = (char)buf[0];
                if (c == '\r')
                    break;
                if (c == '\n')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>BCC = payload 每个字符异或，输出 2 位大写十六进制。</summary>
        public static string CalcBcc (string payload) {
            int xor = 0;
            for (int i = 0; i < payload.Length; i++)
                xor ^= (byte)payload[i];
            return xor.ToString("X2");
        }

        // -------------------- 地址格式（组命令用） --------------------

        /// <summary>
        /// 接点地址：区号 + 5 位十进制。
        /// R100 → R00100；X0 → X00000；Y10 → Y00010
        /// </summary>
        public static string FormatContact (PanasonicAddress addr) {
            char area;
            switch (addr.Area) {
                case PanasonicArea.X: area = 'X'; break;
                case PanasonicArea.Y: area = 'Y'; break;
                case PanasonicArea.R: area = 'R'; break;
                default:
                    throw new ArgumentException("非接点区: " + addr.Area);
            }
            return area + addr.Index.ToString("D5");
        }

        /// <summary>
        /// 数据区地址：常见 WD/RD 使用 D + 5 位（DT200 → D00200）。
        /// WR 使用 W + 5 位（按设备文档可再调）。
        /// </summary>
        public static string FormatDataAddr (PanasonicAddress addr) {
            switch (addr.Area) {
                case PanasonicArea.DT:
                    return "D" + addr.Index.ToString("D5");
                case PanasonicArea.WR:
                    return "W" + addr.Index.ToString("D5");
                default:
                    throw new ArgumentException("非数据区: " + addr.Area);
            }
        }

        // -------------------- 解析（原有） --------------------

        public static PanasonicAddress ParseAddress (string address) {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("地址为空");

            string a = address.Trim().ToUpperInvariant().Replace(" ", "");

            if (a.StartsWith("DT", StringComparison.Ordinal))
                return ParseNumbered(a, 2, PanasonicArea.DT, false);

            if (a.StartsWith("WR", StringComparison.Ordinal))
                return ParseNumbered(a, 2, PanasonicArea.WR, false);

            if (a.StartsWith("X", StringComparison.Ordinal))
                return ParseNumbered(a, 1, PanasonicArea.X, true);

            if (a.StartsWith("Y", StringComparison.Ordinal))
                return ParseNumbered(a, 1, PanasonicArea.Y, true);

            if (a.StartsWith("R", StringComparison.Ordinal))
                return ParseRelay(a);

            throw new ArgumentException("无法解析的松下地址: " + address);
        }

        private static PanasonicAddress ParseRelay (string a) {
            string body = a.Substring(1);
            if (string.IsNullOrEmpty(body))
                throw new ArgumentException("R 地址缺少编号");

            int index;
            bool hasHexLetter = false;
            for (int i = 0; i < body.Length; i++) {
                char c = body[i];
                if (c >= 'A' && c <= 'F')
                    hasHexLetter = true;
                else if (!char.IsDigit(c))
                    throw new ArgumentException("R 地址非法: " + a);
            }

            if (hasHexLetter) {
                if (!int.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index))
                    throw new ArgumentException("R 十六进制地址无效: " + a);
            } else {
                if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    throw new ArgumentException("R 地址无效: " + a);
            }

            if (index < 0)
                throw new ArgumentException("R 地址不能为负");

            return new PanasonicAddress {
                Area = PanasonicArea.R,
                Index = index,
                IsBit = true
            };
        }

        private static PanasonicAddress ParseNumbered (
            string a, int prefixLen, PanasonicArea area, bool isBit) {
            string body = a.Substring(prefixLen);
            int index;
            if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0)
                throw new ArgumentException("地址编号无效: " + a);

            return new PanasonicAddress {
                Area = area,
                Index = index,
                IsBit = isBit
            };
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

    internal enum PanasonicArea {
        X,
        Y,
        R,
        DT,
        WR
    }

    internal struct PanasonicAddress {
        public PanasonicArea Area;
        public int Index;
        public bool IsBit;
    }
}