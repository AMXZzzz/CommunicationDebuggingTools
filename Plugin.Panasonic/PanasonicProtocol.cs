using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace Plugin.Panasonic {
    /// <summary>
    /// 松下 MEWTOCOL-COM 协议插件。
    /// 接点读/写：RCS / WCS；数据字读/写：RD / WD。
    /// 地址示例：R100、X0、Y10、DT200、WR0。
    ///
    /// 数据类型与寄存器字数对应：
    ///   Bool / Int16 / UInt16 → 1 个字（count = 01）
    ///   Int32 / UInt32 / Float → 2 个字（count = 02），低字/高字顺序由 WordOrder 决定
    ///   Double → 4 个字（count = 04）
    /// </summary>
    public sealed class PanasonicProtocol : IProtocol, IProtocolDataAccess, IDisposable {
        private readonly PanasonicSession _session = new PanasonicSession();
        private bool _disposed;

        public bool IsConnected => _session.IsConnected;

        public string GetProtocolName () => "Panasonic MEWTOCOL";

        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null) throw new ArgumentNullException("context");

            _session.Disconnect();
            if (string.IsNullOrWhiteSpace(context.Ip)) return false;

            _session.ApplySettingsJson(context.ProtocolSettingsJson);
            try {
                int port = context.Port > 0 ? context.Port : 9094;
                await _session.ConnectAsync(
                    context.Ip, port,
                    context.TimeoutMs > 0 ? context.TimeoutMs : 3000,
                    cancellationToken);
                return true;
            } catch {
                _session.Disconnect();
                return false;
            }
        }

        public void Disconnect () => _session.Disconnect();

        // ══════════════════════════════════════════════
        //  读
        // ══════════════════════════════════════════════
        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null) throw new ArgumentNullException("request");
            if (!_session.IsConnected) return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                // ── 触点 ──
                if (addr.IsBit || request.DataType == VariableDataType.Bool) {
                    string cmd  = "RCS" + PanasonicSession.FormatContact(addr);
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    request.Value = ParseContactValue(resp);
                }
                // ── 32位：Float / Int32 / UInt32 ──
                else if (Is32Bit(request.DataType)) {
                    string cmd  = "RD" + PanasonicSession.FormatDataAddr(addr) + "02";
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    string hex  = ExtractRdHex(resp, 2);
                    request.Value = Decode32(hex, request.DataType, request.WordOrder);
                }
                // ── 64位：Double ──
                else if (request.DataType == VariableDataType.Double) {
                    string cmd  = "RD" + PanasonicSession.FormatDataAddr(addr) + "04";
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    string hex  = ExtractRdHex(resp, 4);
                    request.Value = DecodeDouble(hex, request.WordOrder);
                }
                // ── 16位：Int16 / UInt16 及其他 ──
                else {
                    string cmd  = "RD" + PanasonicSession.FormatDataAddr(addr) + "01";
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    string hex  = ExtractRdHex(resp, 1);
                    ushort word = ushort.Parse(hex, NumberStyles.HexNumber);
                    request.Value = ConvertWord16(word, request.DataType);
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

        // ══════════════════════════════════════════════
        //  写
        // ══════════════════════════════════════════════
        public Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null) throw new ArgumentNullException("request");
            if (!_session.IsConnected) return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                // ── 触点 ──
                if (addr.IsBit || request.DataType == VariableDataType.Bool) {
                    bool bit = ToBool(request.Value);
                    string cmd = "WCS"
                        + PanasonicSession.FormatContact(addr)
                        + (bit ? "1" : "0");
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                }
                // ── 32位：Float / Int32 / UInt32 ──
                else if (Is32Bit(request.DataType)) {
                    string data = Encode32(request.Value, request.DataType, request.WordOrder);
                    string cmd = "WD"
                        + PanasonicSession.FormatDataAddr(addr)
                        + "02"          // 2 个字
                        + data;
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                }
                // ── 64位：Double ──
                else if (request.DataType == VariableDataType.Double) {
                    string data = EncodeDouble(request.Value, request.WordOrder);
                    string cmd = "WD"
                        + PanasonicSession.FormatDataAddr(addr)
                        + "04"          // 4 个字
                        + data;
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                }
                // ── 16位 ──
                else {
                    ushort word = ToUInt16(request.Value);
                    string cmd = "WD"
                        + PanasonicSession.FormatDataAddr(addr)
                        + "01"          // 1 个字
                        + word.ToString("X4");
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
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

        // ══════════════════════════════════════════════
        //  响应解析
        // ══════════════════════════════════════════════

        private static void EnsureOk (string resp) {
            if (string.IsNullOrWhiteSpace(resp))
                throw new Exception("空响应");
            if (resp.IndexOf('!') >= 0)
                throw new Exception("PLC 返回错误: " + resp.Trim());
            if (resp.IndexOf('$') < 0 && resp.IndexOf('%') < 0)
                throw new Exception("异常响应: " + resp.Trim());
        }

        /// <summary>
        /// 读触点响应：帧格式 %SS$RCS[V][BCC]，V 在倒数第 3 位。
        /// </summary>
        private static bool ParseContactValue (string resp) {
            if (resp == null || resp.Length < 10)
                throw new Exception("触点响应帧过短: " + resp);
            char v = resp[resp.Length - 3];
            if (v == '1') return true;
            if (v == '0') return false;
            throw new Exception("触点值字符非法('" + v + "'): " + resp);
        }

        /// <summary>
        /// 从 RD 响应中按位置提取数据十六进制串。
        /// 帧格式：%SS$RD[words×4hex][BCC2]
        /// </summary>
        private static string ExtractRdHex (string resp, int words) {
            int pos = resp.IndexOf("$RD", StringComparison.OrdinalIgnoreCase);
            if (pos < 0) throw new Exception("RD 响应格式错误: " + resp);
            pos += 3;                    // 跳过 "$RD"
            int needed = words * 4;
            if (resp.Length < pos + needed + 2)
                throw new Exception("RD 响应数据不足（需 " + needed + " 字符）: " + resp);
            return resp.Substring(pos, needed);
        }

        // ══════════════════════════════════════════════
        //  编解码：32位（Float / Int32 / UInt32）
        // ══════════════════════════════════════════════

        /// <summary>
        /// 按 WordOrder 把 hex 串（8字符）解码为 Float / Int32 / UInt32。
        /// LowWordFirst ：hex[0..3]=低字，hex[4..7]=高字
        /// HighWordFirst：hex[0..3]=高字，hex[4..7]=低字
        /// </summary>
        private static object Decode32 (string hex, VariableDataType t, WordOrder wordOrder) {
            if (hex.Length < 8) throw new Exception("32位数据不足 8 字符: " + hex);

            ushort w0 = ushort.Parse(hex.Substring(0, 4), NumberStyles.HexNumber);
            ushort w1 = ushort.Parse(hex.Substring(4, 4), NumberStyles.HexNumber);

            ushort wLow, wHigh;
            if (wordOrder == WordOrder.LowWordFirst) {
                wLow = w0;
                wHigh = w1;
            } else {
                wHigh = w0;
                wLow = w1;
            }

            byte[] bytes = new byte[4];
            byte[] lb = BitConverter.GetBytes(wLow);
            byte[] hb = BitConverter.GetBytes(wHigh);
            bytes[0] = lb[0]; bytes[1] = lb[1];
            bytes[2] = hb[0]; bytes[3] = hb[1];

            switch (t) {
                case VariableDataType.Float: return BitConverter.ToSingle(bytes, 0);
                case VariableDataType.Int32: return BitConverter.ToInt32(bytes, 0);
                case VariableDataType.UInt32: return BitConverter.ToUInt32(bytes, 0);
                default: return BitConverter.ToUInt32(bytes, 0);
            }
        }

        /// <summary>按 WordOrder 把 Float / Int32 / UInt32 编码为 8字符十六进制。</summary>
        private static string Encode32 (object value, VariableDataType t, WordOrder wordOrder) {
            byte[] bytes;
            switch (t) {
                case VariableDataType.Float:
                    bytes = BitConverter.GetBytes(ToFloat(value));
                    break;
                case VariableDataType.UInt32:
                    bytes = BitConverter.GetBytes(ToUInt32(value));
                    break;
                default: // Int32
                    bytes = BitConverter.GetBytes(ToInt32(value));
                    break;
            }

            // bytes[0..1] = 低字, bytes[2..3] = 高字（BitConverter 小端）
            ushort wLow  = BitConverter.ToUInt16(bytes, 0);
            ushort wHigh = BitConverter.ToUInt16(bytes, 2);

            if (wordOrder == WordOrder.LowWordFirst)
                return wLow.ToString("X4") + wHigh.ToString("X4");
            else
                return wHigh.ToString("X4") + wLow.ToString("X4");
        }

        // ══════════════════════════════════════════════
        //  编解码：64位（Double）
        // ══════════════════════════════════════════════

        private static object DecodeDouble (string hex, WordOrder wordOrder) {
            if (hex.Length < 16) throw new Exception("64位数据不足 16 字符: " + hex);

            ushort[] words = new ushort[4];
            for (int i = 0; i < 4; i++)
                words[i] = ushort.Parse(hex.Substring(i * 4, 4), NumberStyles.HexNumber);

            byte[] bytes = new byte[8];
            if (wordOrder == WordOrder.LowWordFirst) {
                // words[0]=低字 … words[3]=高字
                for (int i = 0; i < 4; i++) {
                    byte[] wb = BitConverter.GetBytes(words[i]);
                    bytes[i * 2] = wb[0];
                    bytes[i * 2 + 1] = wb[1];
                }
            } else {
                // words[0]=高字 … words[3]=低字
                for (int i = 0; i < 4; i++) {
                    byte[] wb = BitConverter.GetBytes(words[3 - i]);
                    bytes[i * 2] = wb[0];
                    bytes[i * 2 + 1] = wb[1];
                }
            }
            return BitConverter.ToDouble(bytes, 0);
        }

        private static string EncodeDouble (object value, WordOrder wordOrder) {
            double d;
            if (value is double dv) d = dv;
            else if (value is float fv) d = fv;
            else double.TryParse(value?.ToString() ?? "0", NumberStyles.Any,
                                 CultureInfo.InvariantCulture, out d);

            byte[] bytes  = BitConverter.GetBytes(d);
            ushort[] words = new ushort[4];
            for (int i = 0; i < 4; i++)
                words[i] = BitConverter.ToUInt16(bytes, i * 2);

            // words[0]=低字, words[3]=高字
            if (wordOrder == WordOrder.LowWordFirst)
                return words[0].ToString("X4") + words[1].ToString("X4")
                     + words[2].ToString("X4") + words[3].ToString("X4");
            else
                return words[3].ToString("X4") + words[2].ToString("X4")
                     + words[1].ToString("X4") + words[0].ToString("X4");
        }

        // ══════════════════════════════════════════════
        //  16位转换
        // ══════════════════════════════════════════════

        private static object ConvertWord16 (ushort word, VariableDataType t) {
            switch (t) {
                case VariableDataType.Int16: return (short)word;
                case VariableDataType.UInt16: return word;
                default: return word;
            }
        }

        // ══════════════════════════════════════════════
        //  值类型转换辅助
        // ══════════════════════════════════════════════

        private static bool Is32Bit (VariableDataType t) =>
            t == VariableDataType.Float ||
            t == VariableDataType.Int32 ||
            t == VariableDataType.UInt32;

        private static float ToFloat (object value) {
            if (value is float f) return f;
            if (value is double d) return (float)d;
            float r;
            if (float.TryParse(value?.ToString() ?? "",
                    NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r;
            return 0f;
        }

        private static int ToInt32 (object value) {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is float f) return (int)f;
            if (value is double d) return (int)d;
            int r;
            if (int.TryParse(value?.ToString() ?? "",
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            double dbl;
            if (double.TryParse(value?.ToString() ?? "",
                    NumberStyles.Any, CultureInfo.InvariantCulture, out dbl)) return (int)dbl;
            return 0;
        }

        private static uint ToUInt32 (object value) {
            if (value is uint u) return u;
            if (value is int i) return (uint)i;
            if (value is float f) return (uint)(int)f;
            if (value is double d) return (uint)(int)d;
            uint r;
            if (uint.TryParse(value?.ToString() ?? "",
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return 0;
        }

        private static ushort ToUInt16 (object value) {
            if (value is ushort u) return u;
            if (value is short s) return (ushort)s;
            if (value is int i) return (ushort)i;
            if (value is long l) return (ushort)l;
            double d;
            if (double.TryParse(value?.ToString() ?? "",
                    NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return (ushort)(int)d;
            return 0;
        }

        private static bool ToBool (object value) {
            if (value is bool b) return b;
            if (value == null) return false;
            string s = value.ToString().Trim();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n != 0;
            return false;
        }

        private static ProtocolDataMessage Fail (ProtocolDataMessage request, string message) {
            request.Success = false;
            request.Quality = DataQuality.Bad;
            request.ErrorMessage = message ?? "";
            return request;
        }


        /// <summary>
        /// 探针：先做 Socket.Poll 快速判断 TCP 层，通了再发 RCS R0000 验证协议层。
        /// 返回 false 可能是 TCP 断线也可能是通讯异常，调用方通过 IsConnected 区分。
        /// </summary>
        public Task<bool> PingAsync (CancellationToken cancellationToken) {
            if (!IsConnected)
                return Task.FromResult(false);

            try {
                cancellationToken.ThrowIfCancellationRequested();
                // R0 → R00000，再 RCS
                var addr = PanasonicSession.ParseAddress("R0");
                string contact = PanasonicSession.FormatContact(addr);
                string resp = _session.Transact("RCS" + contact);
                return Task.FromResult(resp != null && resp.IndexOf('$') >= 0);
            } catch {
                return Task.FromResult(false); // 不主动 Disconnect，交给 DeviceService
            }
        }



        public void Dispose () {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}