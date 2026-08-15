using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Attributes;
using CommunicationDebuggingTools.Core.Config;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace Plugin.Panasonic {

    /// <summary>
    /// 松下 MEWTOCOL-COM 协议插件。
    /// 地址 / 站号 / 报文只在本插件与 Session 内处理；UI/Business 不解析。
    /// </summary>
    [ProtocolName("Panasonic MEWTOCOL")]

    public sealed class PanasonicProtocol : IProtocol {

        private readonly PanasonicSession _session = new PanasonicSession();
        private bool _disposed;

        public bool IsConnected => _session.IsConnected;

        /// <summary>
        /// 建连：Ip/Port/Timeout 来自上下文；站号只用 <see cref="ProtocolConnectionContext.StationNo"/>。
        /// ExtraSettingsJson 一期不解析。
        /// </summary>
        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _session.Disconnect();
            if (string.IsNullOrWhiteSpace(context.Ip))
                return false;

            _session.SetStation(context.StationNo);

            try {
                int port = context.Port > 0 ? context.Port : 9094;
                int timeout = context.TimeoutMs > 0 ? context.TimeoutMs : AppConfig.DefaultTimeoutMs;
                await _session.ConnectAsync(context.Ip, port, timeout, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            } catch {
                _session.Disconnect();
                return false;
            }
        }
        /// <summary>
        /// 由 Protocol 在 ConnectAsync 前写入，值来自 context.StationNo。
        /// </summary>
        public void SetStation (int station) {
            if (station < 0) station = 0;
            if (station > 99) station = 99;
            Station = station;
        }

        public void Disconnect () => _session.Disconnect();

        /// <summary>探针：读接点 X0（RCS）。</summary>
        public Task<bool> PingAsync (CancellationToken cancellationToken) {
            if (!IsConnected)
                return Task.FromResult(false);
            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress("X0");
                string body = "RCS" + PanasonicSession.FormatContact(addr);
                string resp = _session.Transact(body);
                return Task.FromResult(IsAckOk(resp));
            } catch {
                return Task.FromResult(false);
            }
        }

        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                if (request.DataType == VariableDataType.Bool || addr.IsBit) {
                    string body = "RCS" + PanasonicSession.FormatContact(addr);
                    string resp = _session.Transact(body);
                    EnsureNoError(resp);
                    // 成功应答典型：%SS$RC0 / %SS$RC1 等，末位 0/1
                    request.Value = ParseContactValue(resp);
                } else {
                    int wordCount = WordsNeeded(request.DataType, request.Length);
                    string start = PanasonicSession.FormatDataAddr(addr);
                    string body = "RD" + start + wordCount.ToString("D4");
                    string resp = _session.Transact(body);
                    EnsureNoError(resp);
                    ushort[] words = ParseDataWords(resp, wordCount);
                    request.Value = FromWords(words, request.DataType, request.WordOrder, request.ByteOrder, request.Length, request.StringEncoding);
                }

                request.Success = true;
                request.Quality = DataQuality.Good;
                request.ErrorMessage = "";
                return Task.FromResult(request);
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        public Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                if (request.DataType == VariableDataType.Bool || addr.IsBit) {
                    bool on = ToBool(request.Value);
                    string body = "WCS" + PanasonicSession.FormatContact(addr) + (on ? "1" : "0");
                    string resp = _session.Transact(body);
                    EnsureNoError(resp);
                } else {
                    ushort[] words = ToWords(
                        request.Value,
                        request.DataType,
                        request.WordOrder,
                        request.ByteOrder,
                        request.Length,
                        request.StringEncoding);
                    string start = PanasonicSession.FormatDataAddr(addr);
                    // WD + 起始 + 数据(每字 4 位十六进制)
                    var sb = new System.Text.StringBuilder();
                    sb.Append("WD").Append(start);
                    for (int i = 0; i < words.Length; i++)
                        sb.Append(words[i].ToString("X4"));
                    string resp = _session.Transact(sb.ToString());
                    EnsureNoError(resp);
                }

                request.Success = true;
                request.Quality = DataQuality.Good;
                request.ErrorMessage = "";
                return Task.FromResult(request);
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        // -------------------- 应答与编解码 --------------------

        /// <summary>MEWTOCOL 错误应答含 ! 或 !ERR。</summary>
        private static void EnsureNoError (string resp) {
            if (string.IsNullOrEmpty(resp))
                throw new Exception("空响应");
            if (resp.IndexOf('!') >= 0)
                throw new Exception("MEWTOCOL 错误: " + resp);
        }

        private static bool IsAckOk (string resp) {
            if (string.IsNullOrEmpty(resp)) return false;
            return resp.IndexOf('!') < 0;
        }

        /// <summary>从 RCS 应答中取接点 0/1。</summary>
        private static bool ParseContactValue (string resp) {
            // 常见：%01$RC1 或 ...RC0
            for (int i = resp.Length - 1; i >= 0; i--) {
                char c = resp[i];
                if (c == '0') return false;
                if (c == '1') return true;
            }
            throw new Exception("无法解析接点值: " + resp);
        }

        /// <summary>从 RD 应答中解析十六进制字。</summary>
        private static ushort[] ParseDataWords (string resp, int wordCount) {
            // 数据区在 $RD 之后为连续 4 位十六进制
            int idx = resp.IndexOf("$RD", StringComparison.OrdinalIgnoreCase);
            string data = idx >= 0 ? resp.Substring(idx + 3) : resp;
            // 去掉可能的前缀杂字符，只保留 0-9A-F
            var hex = new System.Text.StringBuilder();
            for (int i = 0; i < data.Length; i++) {
                char c = data[i];
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))
                    hex.Append(c);
            }
            string h = hex.ToString();
            if (h.Length < wordCount * 4)
                throw new Exception("数据字不足: " + resp);

            ushort[] words = new ushort[wordCount];
            for (int i = 0; i < wordCount; i++) {
                words[i] = ushort.Parse(h.Substring(i * 4, 4), NumberStyles.HexNumber);
            }
            return words;
        }

        private static int WordsNeeded (VariableDataType dt, int length) {
            switch (dt) {
                case VariableDataType.Int16:
                case VariableDataType.UInt16:
                    return 1;
                case VariableDataType.Int32:
                case VariableDataType.UInt32:
                case VariableDataType.Float:
                    return 2;
                case VariableDataType.Int64:
                case VariableDataType.UInt64:
                case VariableDataType.Double:
                    return 4;
                case VariableDataType.String:
                    return length > 0 ? (length + 1) / 2 : 1;
                default:
                    return 1;
            }
        }

        private static object FromWords (
            ushort[] w,
            VariableDataType dt,
            WordOrder wordOrder,
            ByteOrder byteOrder,
            int length,
            StringEncodingKind encoding) {
            // 简化：字内按大端拼；多字按 HighWordFirst 时 w[0] 为高字
            switch (dt) {
                case VariableDataType.Int16:
                    return (short)w[0];
                case VariableDataType.UInt16:
                    return w[0];
                case VariableDataType.Int32: {
                    uint u = Combine2(w, wordOrder);
                    return (int)u;
                }
                case VariableDataType.UInt32:
                    return Combine2(w, wordOrder);
                case VariableDataType.Float: {
                    uint u = Combine2(w, wordOrder);
                    byte[] le = BitConverter.GetBytes(u);
                    return BitConverter.ToSingle(le, 0);
                }
                case VariableDataType.Double: {
                    ulong u = Combine4(w, wordOrder);
                    byte[] le = BitConverter.GetBytes(u);
                    return BitConverter.ToDouble(le, 0);
                }
                case VariableDataType.Bool:
                    return w[0] != 0;
                default:
                    return w[0];
            }
        }

        private static ushort[] ToWords (
            object value,
            VariableDataType dt,
            WordOrder wordOrder,
            ByteOrder byteOrder,
            int length,
            StringEncodingKind encoding) {
            switch (dt) {
                case VariableDataType.Int16:
                    return new ushort[] { (ushort)(short)ToInt64(value) };
                case VariableDataType.UInt16:
                    return new ushort[] { (ushort)ToInt64(value) };
                case VariableDataType.Int32:
                case VariableDataType.UInt32: {
                    uint u = (uint)ToInt64(value);
                    return Split2(u, wordOrder);
                }
                case VariableDataType.Float: {
                    float f = ToFloat(value);
                    uint u = BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
                    return Split2(u, wordOrder);
                }
                case VariableDataType.Double: {
                    double d = ToDouble(value);
                    ulong u = BitConverter.ToUInt64(BitConverter.GetBytes(d), 0);
                    return Split4(u, wordOrder);
                }
                case VariableDataType.Bool:
                    return new ushort[] { (ushort)(ToBool(value) ? 1 : 0) };
                default:
                    throw new Exception("不支持写入类型: " + dt);
            }
        }

        private static uint Combine2 (ushort[] w, WordOrder order) {
            if (w == null || w.Length < 2) return w != null && w.Length > 0 ? w[0] : 0u;
            if (order == WordOrder.LowWordFirst)
                return (uint)w[0] | ((uint)w[1] << 16);
            return (uint)w[1] | ((uint)w[0] << 16);
        }

        private static ulong Combine4 (ushort[] w, WordOrder order) {
            if (w == null || w.Length < 4) return 0;
            // HighWordFirst: w0 最高
            if (order == WordOrder.LowWordFirst)
                return (ulong)w[0] | ((ulong)w[1] << 16) | ((ulong)w[2] << 32) | ((ulong)w[3] << 48);
            return (ulong)w[3] | ((ulong)w[2] << 16) | ((ulong)w[1] << 32) | ((ulong)w[0] << 48);
        }

        private static ushort[] Split2 (uint u, WordOrder order) {
            ushort lo = (ushort)(u & 0xFFFF);
            ushort hi = (ushort)(u >> 16);
            if (order == WordOrder.LowWordFirst)
                return new ushort[] { lo, hi };
            return new ushort[] { hi, lo };
        }

        private static ushort[] Split4 (ulong u, WordOrder order) {
            ushort w0 = (ushort)(u & 0xFFFF);
            ushort w1 = (ushort)((u >> 16) & 0xFFFF);
            ushort w2 = (ushort)((u >> 32) & 0xFFFF);
            ushort w3 = (ushort)((u >> 48) & 0xFFFF);
            if (order == WordOrder.LowWordFirst)
                return new ushort[] { w0, w1, w2, w3 };
            return new ushort[] { w3, w2, w1, w0 };
        }

        private static bool ToBool (object v) {
            if (v is bool b) return b;
            if (v == null) return false;
            string s = v.ToString().Trim();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("ON", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                return false;
            long n;
            return long.TryParse(s, out n) && n != 0;
        }

        private static long ToInt64 (object v) {
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is short s) return s;
            if (v is ushort us) return us;
            if (v is uint u) return u;
            long r;
            if (long.TryParse(v?.ToString() ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out r))
                return r;
            return 0;
        }

        private static float ToFloat (object v) {
            if (v is float f) return f;
            if (v is double d) return (float)d;
            float r;
            float.TryParse(v?.ToString() ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out r);
            return r;
        }

        private static double ToDouble (object v) {
            if (v is double d) return d;
            if (v is float f) return f;
            double r;
            double.TryParse(v?.ToString() ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out r);
            return r;
        }

        private static ProtocolDataMessage Fail (ProtocolDataMessage req, string msg) {
            req.Success = false;
            req.Quality = DataQuality.Bad;
            req.ErrorMessage = msg ?? "";
            return req;
        }

        public void Dispose () {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}