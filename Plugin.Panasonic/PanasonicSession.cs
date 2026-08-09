using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Panasonic {
    /// <summary>
    /// 松下 MEWTOCOL 会话：站号、地址解析、TCP。
    /// 报文收发可在本类后续补全。
    /// </summary>
    internal sealed class PanasonicSession : IDisposable {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private int _timeoutMs = 3000;
        private bool _disposed;

        /// <summary>MEWTOCOL 站号（1–99 等，按设备约定）。</summary>
        public int Station { get; private set; } = 1;

        public bool IsConnected =>
            _tcp != null && _tcp.Connected && _stream != null;

        public int TimeoutMs {
            get => _timeoutMs;
            set => _timeoutMs = value < 500 ? 500 : value;
        }

        /// <summary>从 ProtocolSettingsJson 读取 station。</summary>
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

        /// <summary>
        /// 解析松下地址。支持：
        /// X0 / Y0 / R100 / R1A（十六进制触点）/
        /// DT0 / DT100 / WR0
        /// </summary>
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
        /// R 区：十进制 R100，或十六进制触点 R1A（含 A–F）。
        /// </summary>
        private static PanasonicAddress ParseRelay (string a) {
            string body = a.Substring(1);
            if (string.IsNullOrEmpty(body))
                throw new ArgumentException("R 地址缺少编号");

            int index;
            // 含 A-F → 按十六进制（如 R1A）
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