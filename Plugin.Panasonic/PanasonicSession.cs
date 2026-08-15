using CommunicationDebuggingTools.Core.Tools;
using System;
using CommunicationDebuggingTools.Core.Config;
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
        private const int DEFAULT_PORT = 9094;

        private TcpClient _tcp;
        private NetworkStream _stream;
        private int _timeoutMs = AppConfig.DefaultTimeoutMs;
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

        public async Task ConnectAsync (string ip, int port, int timeoutMs, CancellationToken ct) {
            Disconnect();
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("IP 为空");

            if (port <= 0)
                port = DEFAULT_PORT; // AppConfig 定义协议无关常量，端口默认值保留在插件内

            TimeoutMs = timeoutMs > 0 ? timeoutMs : AppConfig.DefaultTimeoutMs;
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

        public void SetStation (int station) {
            if (station < 0) station = 0;
            if (station > 99) station = 99;
            Station = station;
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
        /// 接点组帧后缀。
        /// R100 → R00100；R10A → R010A（字3位十进制 + 位1位十六进制）；X0 → X00000。
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

            if (addr.Area == PanasonicArea.R && addr.BitIndex >= 0)
                return area + addr.Index.ToString("D3") + addr.BitIndex.ToString("X1");

            return area + addr.Index.ToString("D5");
        }

        /// <summary>
        /// 数据区地址组帧。
        /// 本测试从站（MEWTOCOL Slave）按 4 位十进制解析：DT100 → D0100。
        /// （部分实机文档为 5 位 D00100；若实机不通可再做成可配置。）
        /// </summary>
        public static string FormatDataAddr (PanasonicAddress addr) {
            switch (addr.Area) {
                case PanasonicArea.DT:
                    return "D" + addr.Index.ToString("D4"); // DT100 → D0100
                case PanasonicArea.WR:
                    return "W" + addr.Index.ToString("D4"); // WR0 → W0000
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

            // 含 A–F：末位为十六进制位号，前面为十进制字号（R10A = 字10 位A）
            char last = body[body.Length - 1];
            if (last >= 'A' && last <= 'F') {
                if (body.Length < 2)
                    throw new ArgumentException("R 位地址格式无效: " + a);

                string wordPart = body.Substring(0, body.Length - 1);
                for (int i = 0; i < wordPart.Length; i++) {
                    if (!char.IsDigit(wordPart[i]))
                        throw new ArgumentException("R 字号非法: " + a);
                }

                int word;
                if (!int.TryParse(wordPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out word) || word < 0)
                    throw new ArgumentException("R 字号无效: " + a);

                int bit = Convert.ToInt32(last.ToString(), 16);

                return new PanasonicAddress {
                    Area = PanasonicArea.R,
                    Index = word,
                    BitIndex = bit,
                    IsBit = true
                };
            }

            // 纯数字：十进制接点号 R100
            for (int i = 0; i < body.Length; i++) {
                if (!char.IsDigit(body[i]))
                    throw new ArgumentException("R 地址非法: " + a);
            }

            int index;
            if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0)
                throw new ArgumentException("R 地址无效: " + a);

            return new PanasonicAddress {
                Area = PanasonicArea.R,
                Index = index,
                BitIndex = -1,
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
        /// <summary>十进制接点号，或 word+bit 时的字号。</summary>
        public int Index;
        /// <summary>位号 0–15；-1 表示纯十进制接点（如 R100）。</summary>
        public int BitIndex;
        public bool IsBit;
    }
}