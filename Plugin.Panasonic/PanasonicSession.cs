using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Panasonic {

    /// <summary>
    /// 寄存类型
    /// </summary>
    internal enum PanasonicArea {
        X,
        Y,
        R,
        DT,
        WR
    }

    /// <summary>
    /// 地址类型封包
    /// </summary>
    internal struct PanasonicAddress {
        //! 地址类型
        public PanasonicArea Area;
        //! 地址编号
        public int Index;
        //! 是否为位类型
        public bool IsBit;
    }

    /// <summary>
    /// 松下 MEWTOCOL-COM 会话：站号、地址解析、TCP、报文收发。
    /// 帧格式：% + 站号(2位十六进制) + # + 命令 + BCC(2位十六进制) + CR
    /// BCC：对「站号+#命令」整段逐字节异或。
    /// </summary>
    internal sealed class PanasonicSession : IDisposable {  //! 不允许继承
        //! TCP 客户端
        private TcpClient _tcp;
        //! TCP 网络流
        private NetworkStream _stream;
        //! 读写超时（毫秒）
        private int _timeoutMs = 3000;
        //! 是否已释放
        private bool _disposed;
        //! 线程同步锁
        private readonly object _sync = new object();

        /// <summary>
        /// MEWTOCOL 站号（1–99）。
        /// </summary>
        public int Station { get; private set; } = 1;

        /// <summary>
        /// 连接状态
        /// </summary>
        public bool IsConnected =>
            _tcp != null && _tcp.Connected && _stream != null;

        /// <summary>
        /// 超时
        /// </summary>
        public int TimeoutMs {
            get => _timeoutMs;
            set => _timeoutMs = value < 500 ? 500 : value;
        }

        /// <summary>
        /// 应用设置信息
        /// </summary>
        /// <param name="json"></param>
        public void ApplySettingsJson (string json) {
            Station = ReadIntField(json, "station", 1);
            if (Station < 0) Station = 0;
            if (Station > 99) Station = 99;
        }

        /// <summary>
        /// 连接
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="timeoutMs"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="TimeoutException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task ConnectAsync (string ip, int port, int timeoutMs, CancellationToken ct) {

            Disconnect();

            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("IP 为空");

            if (port <= 0)
                port = 9094;

            TimeoutMs = timeoutMs > 0 ? timeoutMs : 3000;
            _tcp = new TcpClient();

            //! 连接
            var connectTask = _tcp.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(TimeoutMs, ct);
            if (await Task.WhenAny(connectTask, timeoutTask) != connectTask) {
                Disconnect();
                throw new TimeoutException("连接超时");
            }
            await connectTask;

            //! 检查连接状态
            if (!_tcp.Connected || ct.IsCancellationRequested) {
                Disconnect();
                throw new InvalidOperationException("连接失败或已取消");
            }

            //! 连接成功
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = TimeoutMs;
            _stream.WriteTimeout = TimeoutMs;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
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

            //! 拼接站号+命令 payload = SS + # + CMD
            string payload = Station.ToString("X2") + "#" + commandBody;
            string frame = "%" + payload + CalcBcc(payload) + "\r";
            byte[] send = Encoding.ASCII.GetBytes(frame);

            //! 上锁发送，防止多线程同时访问网络流
            lock (_sync) {
                //! 发送
                _stream.Write(send, 0, send.Length);
                //! 立即刷新，确保发送出去
                _stream.Flush();
                //! 读取完成
                return ReadLineCr();
            }
        }

        /// <summary>
        /// 读到 CR 为止（不含 CR）。
        /// </summary>
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

        /// <summary>
        /// BCC = payload 每个字符异或，输出 2 位大写十六进制。
        /// </summary>
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
            // MEWTOCOL-COM 标准：接点编号为 4 位十进制（0000-8999）
            if (addr.Index > 9999)
                throw new ArgumentOutOfRangeException("addr.Index",
                    "MEWTOCOL 接点编号超出范围(0-9999): " + addr.Index);
            return area + addr.Index.ToString("D4");
        }

        /// <summary>
        /// 数据区地址：常见 WD/RD 使用 D + 5 位（DT200 → D00200）。
        /// WR 使用 W + 5 位（按设备文档可再调）。
        /// </summary>
        public static string FormatDataAddr (PanasonicAddress addr) {
            // MEWTOCOL-COM 标准：数据寄存器地址为 4 位十进制（0000-9999）
            if (addr.Index > 9999)
                throw new ArgumentOutOfRangeException("addr.Index",
                    "MEWTOCOL 数据地址超出范围(0-9999): " + addr.Index);
            switch (addr.Area) {
                case PanasonicArea.DT:
                    return "D" + addr.Index.ToString("D4");
                case PanasonicArea.WR:
                    return "W" + addr.Index.ToString("D4");
                default:
                    throw new ArgumentException("非数据区: " + addr.Area);
            }
        }

        // -------------------- 解析（原有） --------------------

        /// <summary>
        /// 解析地址类型
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
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

        /// <summary>
        /// 解析地址为16进制
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
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

        /// <summary>
        /// 解析地址编号
        /// </summary>
        /// <param name="a"></param>
        /// <param name="prefixLen"></param>
        /// <param name="area"></param>
        /// <param name="isBit"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
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
        /// <summary>
        /// 读取Int节点
        /// </summary>
        /// <param name="json"></param>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>

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
}