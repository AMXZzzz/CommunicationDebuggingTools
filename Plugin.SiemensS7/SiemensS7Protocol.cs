using CommunicationDebuggingTools.Core;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.SiemensS7 {
    /// <summary>
    /// Siemens S7 协议插件。
    /// 通过 ISO-on-TCP（端口 102）实现 S7 Read/Write Variable PDU。
    ///
    /// 地址格式：
    ///   DB1.DBX0.0  DB1.DBB0  DB1.DBW0  DB1.DBD0
    ///   M0.0  MB0  MW0  MD0
    ///   I0.0  IB0  IW0  ID0
    ///   Q0.0  QB0  QW0  QD0
    ///
    /// 数据类型与 S7 传输尺寸对应：
    ///   Bool              → Bit  (0x01)
    ///   Int16 / UInt16    → Word (0x04)  2 字节大端
    ///   Int32 / UInt32    → DWord(0x06)  4 字节大端
    ///   Float             → Real (0x08)  4 字节 IEEE-754 大端
    ///   Double            → 2×DWord      8 字节大端
    /// </summary>
    [ProtocolName("Siemens S7")]
    public sealed class SiemensS7Protocol : IProtocol {

        private readonly SiemensS7Session _session = new SiemensS7Session();
        private bool _disposed;

        // S7 传输尺寸代码（请求 item 字段）
        const byte TS_BIT   = 0x01;
        const byte TS_BYTE  = 0x02;
        const byte TS_WORD  = 0x04;
        const byte TS_DWORD = 0x06;
        const byte TS_REAL  = 0x08;

        // S7 区域代码
        const byte AREA_I  = 0x81;
        const byte AREA_Q  = 0x82;
        const byte AREA_M  = 0x83;
        const byte AREA_DB = 0x84;

        public bool IsConnected => _session.IsConnected;

        // ════════════════════════════════════════════════
        //  连接 / 断开
        // ════════════════════════════════════════════════
        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _session.Disconnect();
            if (string.IsNullOrWhiteSpace(context.Ip))
                return false;

            // 仅插件内解析 rack/slot；禁止再用 ProtocolSettingsJson
            _session.ApplySettingsJson(context.ExtraSettingsJson);

            try {
                int port = context.Port > 0 ? context.Port : 102;
                int timeout = context.TimeoutMs > 0 ? context.TimeoutMs : AppConfig.DefaultTimeoutMs;
                await _session.ConnectAsync(context.Ip, port, timeout, cancellationToken);
                return true;
            } catch {
                _session.Disconnect();
                return false;
            }
        }

        public void Disconnect () => _session.Disconnect();

        // ════════════════════════════════════════════════
        //  读
        // ════════════════════════════════════════════════
        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null) throw new ArgumentNullException("request");
            if (!_session.IsConnected) return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                S7Address addr = SiemensS7Session.ParseAddress(request.Address);
                byte      ts   = GetTransportSize(request.DataType, addr);

                // Double 需读 2 个 DWord（8 字节）
                int elemCount = (request.DataType == VariableDataType.Double) ? 2 : 1;

                byte[] job  = BuildReadJob(addr, ts, elemCount, _session.NextRef());
                byte[] resp = _session.Transact(job);

                byte[] raw = ParseReadResponse(resp, ts, elemCount);
                request.Value = FromS7Bytes(raw, request.DataType);
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

        // ════════════════════════════════════════════════
        //  写
        // ════════════════════════════════════════════════
        public Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null) throw new ArgumentNullException("request");
            if (!_session.IsConnected) return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                S7Address addr = SiemensS7Session.ParseAddress(request.Address);
                byte      ts   = GetTransportSize(request.DataType, addr);
                byte[]    data = ToS7Bytes(request.Value, request.DataType);

                byte[] job  = BuildWriteJob(addr, ts, data, _session.NextRef());
                byte[] resp = _session.Transact(job);
                ParseWriteResponse(resp);

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

        // ════════════════════════════════════════════════
        //  PDU 构造
        // ════════════════════════════════════════════════

        /// <summary>
        /// Read Variable Job PDU（0x04）。
        /// 帧结构：S7 头(10) + 参数: func(1)+count(1)+item(12) = 14
        /// </summary>
        static byte[] BuildReadJob (S7Address addr, byte ts, int elemCount, ushort pduRef) {
            byte   areaCode = AreaCode(addr);
            int    bitAddr  = addr.ByteOffset * 8 + (addr.Bit >= 0 ? addr.Bit : 0);

            return new byte[] {
                // S7 头
                0x32, 0x01, 0x00, 0x00,
                (byte)(pduRef >> 8), (byte)(pduRef & 0xFF),
                0x00, 0x0E,                             // param length = 14
                0x00, 0x00,                             // data length  = 0
                // 参数
                0x04,                                   // function = Read
                0x01,                                   // item count = 1
                // item（12 字节）
                0x12, 0x0A, 0x10,                       // var spec, len=10, S7ANY
                ts,                                     // transport size
                (byte)(elemCount >> 8), (byte)(elemCount & 0xFF),
                (byte)(addr.DbNumber >> 8), (byte)(addr.DbNumber & 0xFF),
                areaCode,
                (byte)(bitAddr >> 16), (byte)(bitAddr >> 8), (byte)(bitAddr & 0xFF)
            };
        }

        /// <summary>
        /// Write Variable Job PDU（0x05）。
        /// 帧结构：S7 头(10) + 参数(14) + 数据项头(4) + 数据字节
        /// </summary>
        static byte[] BuildWriteJob (S7Address addr, byte ts, byte[] data, ushort pduRef) {
            byte areaCode = AreaCode(addr);
            int  bitAddr  = addr.ByteOffset * 8 + (addr.Bit >= 0 ? addr.Bit : 0);

            // 数据段：reserved(1) + rts(1) + bitLen(2) + data
            byte rts    = (ts == TS_BIT) ? (byte)0x03 : (byte)0x04;
            int  bitLen = (ts == TS_BIT) ? 1 : data.Length * 8;
            int  pLen   = 14;                          // 参数长度
            int  dLen   = 4 + data.Length;             // 数据段长度

            byte[] pdu = new byte[10 + pLen + dLen];
            int    i   = 0;

            // S7 头
            pdu[i++] = 0x32; pdu[i++] = 0x01; pdu[i++] = 0x00; pdu[i++] = 0x00;
            pdu[i++] = (byte)(pduRef >> 8); pdu[i++] = (byte)(pduRef & 0xFF);
            pdu[i++] = (byte)(pLen >> 8); pdu[i++] = (byte)(pLen & 0xFF);
            pdu[i++] = (byte)(dLen >> 8); pdu[i++] = (byte)(dLen & 0xFF);
            // 参数
            pdu[i++] = 0x05; pdu[i++] = 0x01;         // Write, 1 item
            pdu[i++] = 0x12; pdu[i++] = 0x0A; pdu[i++] = 0x10;
            pdu[i++] = ts;
            pdu[i++] = 0x00; pdu[i++] = 0x01;          // element count = 1
            pdu[i++] = (byte)(addr.DbNumber >> 8); pdu[i++] = (byte)(addr.DbNumber & 0xFF);
            pdu[i++] = areaCode;
            pdu[i++] = (byte)(bitAddr >> 16); pdu[i++] = (byte)(bitAddr >> 8); pdu[i++] = (byte)(bitAddr & 0xFF);
            // 数据项
            pdu[i++] = 0x00;                            // reserved
            pdu[i++] = rts;
            pdu[i++] = (byte)(bitLen >> 8); pdu[i++] = (byte)(bitLen & 0xFF);
            Array.Copy(data, 0, pdu, i, data.Length);
            return pdu;
        }

        // ════════════════════════════════════════════════
        //  响应解析
        // ════════════════════════════════════════════════

        /// <summary>
        /// 解析 Read Ack-Data，返回原始字节。
        /// 帧：32 03 00 00 ref2 pLen2 dLen2 errC errC | param: 04 01 | data: RC RTS BL BL [bytes]
        /// </summary>
        static byte[] ParseReadResponse (byte[] s7, byte ts, int elemCount) {
            if (s7 == null || s7.Length < 14)
                throw new Exception("Read 响应过短");
            if (s7[1] != 0x03)
                throw new Exception("非 Ack-Data");
            if (s7[10] != 0x00 || s7[11] != 0x00)
                throw new Exception("S7 错误 errClass=0x" + s7[10].ToString("X2")
                    + " errCode=0x" + s7[11].ToString("X2"));

            int paramLen = (s7[6] << 8) | s7[7];
            int dOff     = 12 + paramLen;               // 数据段起始

            if (dOff + 4 > s7.Length) throw new Exception("Read 数据段不足");

            byte rc = s7[dOff];
            if (rc != 0xFF)
                throw new Exception("读取失败，返回码 0x" + rc.ToString("X2"));

            byte rts    = s7[dOff + 1];
            int  bitLen = (s7[dOff + 2] << 8) | s7[dOff + 3];
            int  byteLen;
            if (rts == 0x03) byteLen = 1;               // Bit
            else if (ts == TS_REAL || ts == TS_DWORD) byteLen = 4 * elemCount; // Real/DWord
            else byteLen = (bitLen + 7) / 8;

            if (dOff + 4 + byteLen > s7.Length)
                throw new Exception("Read 数据字节不足（需 " + byteLen + " 字节）");

            byte[] data = new byte[byteLen];
            Array.Copy(s7, dOff + 4, data, 0, byteLen);
            return data;
        }

        /// <summary>解析 Write Ack-Data，检查返回码。</summary>
        static void ParseWriteResponse (byte[] s7) {
            if (s7 == null || s7.Length < 12)
                throw new Exception("Write 响应过短");
            if (s7[1] != 0x03)
                throw new Exception("非 Ack-Data");
            if (s7[10] != 0x00 || s7[11] != 0x00)
                throw new Exception("S7 错误 errClass=0x" + s7[10].ToString("X2")
                    + " errCode=0x" + s7[11].ToString("X2"));

            int paramLen = (s7[6] << 8) | s7[7];
            int dOff     = 12 + paramLen;
            if (dOff >= s7.Length) throw new Exception("Write 响应无数据");

            byte rc = s7[dOff];
            if (rc != 0xFF)
                throw new Exception("写入失败，返回码 0x" + rc.ToString("X2"));
        }

        // ════════════════════════════════════════════════
        //  类型转换（S7 全程大端）
        // ════════════════════════════════════════════════

        /// <summary>S7 原始字节 → .NET 值（大端解码）。</summary>
        static object FromS7Bytes (byte[] d, VariableDataType dt) {
            switch (dt) {
                case VariableDataType.Bool:
                    return d[0] != 0x00;

                case VariableDataType.Int16:
                    return (short)((d[0] << 8) | d[1]);

                case VariableDataType.UInt16:
                    return (ushort)((d[0] << 8) | d[1]);

                case VariableDataType.Int32:
                    return (d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3];

                case VariableDataType.UInt32:
                    return (uint)((d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3]);

                case VariableDataType.Float: {
                    // S7 大端 → 小端 BitConverter
                    byte[] le = new byte[] { d[3], d[2], d[1], d[0] };
                    return BitConverter.ToSingle(le, 0);
                }

                case VariableDataType.Double: {
                    // 8 字节，大端
                    byte[] le = new byte[] { d[7], d[6], d[5], d[4], d[3], d[2], d[1], d[0] };
                    return BitConverter.ToDouble(le, 0);
                }

                default:
                    return d;
            }
        }

        /// <summary>.NET 值 → S7 原始字节（大端编码）。</summary>
        static byte[] ToS7Bytes (object value, VariableDataType dt) {
            switch (dt) {
                case VariableDataType.Bool:
                    return new byte[] { (byte)(ToBool(value) ? 1 : 0) };

                case VariableDataType.Int16: {
                    short s = (short)ToInt64(value);
                    return new byte[] { (byte)(s >> 8), (byte)(s & 0xFF) };
                }

                case VariableDataType.UInt16: {
                    ushort u = (ushort)ToInt64(value);
                    return new byte[] { (byte)(u >> 8), (byte)(u & 0xFF) };
                }

                case VariableDataType.Int32: {
                    int v = (int)ToInt64(value);
                    return new byte[] {
                        (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)(v & 0xFF) };
                }

                case VariableDataType.UInt32: {
                    uint u = (uint)ToInt64(value);
                    return new byte[] {
                        (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)(u & 0xFF) };
                }

                case VariableDataType.Float: {
                    byte[] le = BitConverter.GetBytes(ToFloat(value));
                    return new byte[] { le[3], le[2], le[1], le[0] }; // 翻转为大端
                }

                case VariableDataType.Double: {
                    byte[] le = BitConverter.GetBytes(ToDouble(value));
                    return new byte[] { le[7], le[6], le[5], le[4], le[3], le[2], le[1], le[0] };
                }

                default:
                    throw new Exception("不支持的数据类型写入: " + dt);
            }
        }

        // ════════════════════════════════════════════════
        //  辅助
        // ════════════════════════════════════════════════

        /// <summary>根据 DataType 选取 S7 传输尺寸；Bool 优先用地址本身的 Bit。</summary>
        static byte GetTransportSize (VariableDataType dt, S7Address addr) {
            switch (dt) {
                case VariableDataType.Bool: return TS_BIT;
                case VariableDataType.Int16:
                case VariableDataType.UInt16: return TS_WORD;
                case VariableDataType.Int32:
                case VariableDataType.UInt32: return TS_DWORD;
                case VariableDataType.Float: return TS_REAL;
                case VariableDataType.Double: return TS_DWORD;  // 读 2 个 DWord
                default:
                    switch (addr.Size) {
                        case S7TransportSize.Bit: return TS_BIT;
                        case S7TransportSize.Byte: return TS_BYTE;
                        case S7TransportSize.Word: return TS_WORD;
                        case S7TransportSize.DWord: return TS_DWORD;
                        default: return TS_WORD;
                    }
            }
        }

        const byte AREA_V = 0x87;   // S7-200 V memory / Local data

        static byte AreaCode (S7Address addr) {
            switch (addr.Area) {
                case 'D': return AREA_DB;
                case 'I': return AREA_I;
                case 'Q': return AREA_Q;
                case 'M': return AREA_M;
                case 'V': return AREA_V;
                default: throw new Exception("不支持的区域: " + addr.Area);
            }
        }

        static bool ToBool (object v) {
            if (v is bool b) return b;
            if (v == null) return false;
            string s = v.ToString().Trim();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            long n; if (long.TryParse(s, out n)) return n != 0;
            return false;
        }

        static long ToInt64 (object v) {
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is short s) return s;
            if (v is ushort us) return us;
            if (v is uint u) return u;
            if (v is float f) return (long)f;
            if (v is double d) return (long)d;
            long r; double dr;
            if (long.TryParse(v?.ToString() ?? "", out r)) return r;
            if (double.TryParse(v?.ToString() ?? "", NumberStyles.Any,
                    CultureInfo.InvariantCulture, out dr)) return (long)dr;
            return 0;
        }

        static float ToFloat (object v) {
            if (v is float f) return f;
            if (v is double d) return (float)d;
            float r;
            if (float.TryParse(v?.ToString() ?? "", NumberStyles.Any,
                    CultureInfo.InvariantCulture, out r)) return r;
            return 0f;
        }

        static double ToDouble (object v) {
            if (v is double d) return d;
            if (v is float f) return f;
            double r;
            if (double.TryParse(v?.ToString() ?? "", NumberStyles.Any,
                    CultureInfo.InvariantCulture, out r)) return r;
            return 0.0;
        }

        static ProtocolDataMessage Fail (ProtocolDataMessage req, string msg) {
            req.Success = false;
            req.Quality = DataQuality.Bad;
            req.ErrorMessage = msg ?? "";
            return req;
        }


        /// <summary>
        /// 探针：先做 Socket.Poll 检测 TCP 层，通了再读 MB0 验证 S7 通讯层。
        /// </summary>
        public Task<bool> PingAsync (System.Threading.CancellationToken cancellationToken) {
            // ① TCP 层
            if (!IsConnected) return Task.FromResult(false);
            // ② S7 协议层：读 MB0（M 区字节 0，安全地址）
            try {
                cancellationToken.ThrowIfCancellationRequested();
                S7Address addr = SiemensS7Session.ParseAddress("MB0");
                byte[] job  = BuildReadJob(addr, TS_BYTE, 1, _session.NextRef());
                byte[] resp = _session.Transact(job);
                return Task.FromResult(resp != null && resp.Length > 1 && resp[1] == 0x03);
            } catch {
                return Task.FromResult(false);
            }
        }
        public void Dispose () {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}