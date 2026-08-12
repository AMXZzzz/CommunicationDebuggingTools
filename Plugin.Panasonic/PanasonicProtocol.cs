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
    /// </summary>
    public sealed class PanasonicProtocol : IProtocol, IProtocolDataAccess, IDisposable {
        private readonly PanasonicSession _session = new PanasonicSession();
        private bool _disposed;

        public bool IsConnected => _session.IsConnected;

        public string GetProtocolName () => "Panasonic MEWTOCOL";

        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null)
                throw new ArgumentNullException("context");

            _session.Disconnect();
            if (string.IsNullOrWhiteSpace(context.Ip))
                return false;

            _session.ApplySettingsJson(context.ProtocolSettingsJson);

            try {
                int port = context.Port > 0 ? context.Port : 9094;
                await _session.ConnectAsync(
                    context.Ip,
                    port,
                    context.TimeoutMs > 0 ? context.TimeoutMs : 3000,
                    cancellationToken);
                return true;
            } catch {
                _session.Disconnect();
                return false;
            }
        }

        public void Disconnect () => _session.Disconnect();

        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException("request");
            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                if (addr.IsBit || request.DataType == VariableDataType.Bool) {
                    string cmd = "RCS" + PanasonicSession.FormatContact(addr);
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    request.Value = ParseContactValue(resp);
                } else {
                    // RD + 地址 + 字数(0001)
                    string cmd = "RD" + PanasonicSession.FormatDataAddr(addr) + "0001";
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                    ushort word = ParseDataWord(resp);
                    request.Value = ConvertWord(word, request.DataType);
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
                throw new ArgumentNullException("request");
            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                PanasonicAddress addr = PanasonicSession.ParseAddress(request.Address);

                if (addr.IsBit || request.DataType == VariableDataType.Bool) {
                    bool bit = ToBool(request.Value);
                    string cmd = "WCS"
                        + PanasonicSession.FormatContact(addr)
                        + (bit ? "1" : "0");
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                } else {
                    // 写数据字：WD + 地址 + 字数(0001) + 数据
                    ushort word = ToUInt16(request.Value);
                    string cmd = "WD"
                                + PanasonicSession.FormatDataAddr(addr)
                                + "0001"                          // 1 个字
                                + word.ToString("X4");
                    string resp = _session.Transact(cmd);
                    EnsureOk(resp);
                }

                request.Success = true;
                request.Quality = DataQuality.Good;
                request.ErrorMessage = "";
                // 成功后记住写入值
                if (request.Value == null)
                    request.Value = request.Value;
                return Task.FromResult(request);
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        // -------------------- 响应解析 --------------------

        private static void EnsureOk (string resp) {
            if (string.IsNullOrWhiteSpace(resp))
                throw new Exception("空响应");

            // 错误帧常含 '!'
            if (resp.IndexOf('!') >= 0)
                throw new Exception("PLC 返回错误: " + resp.Trim());

            // 正常应答通常含 '$'（%ss$...）
            if (resp.IndexOf('$') < 0 && resp.IndexOf('%') < 0)
                throw new Exception("异常响应: " + resp.Trim());
        }

        /// <summary>从响应中取接点 0/1。</summary>
        private static bool ParseContactValue (string resp) {
            for (int i = resp.Length - 1; i >= 0; i--) {
                if (resp[i] == '0' || resp[i] == '1')
                    return resp[i] == '1';
            }
            throw new Exception("无法解析接点值: " + resp);
        }

        /// <summary>从响应中取末尾 4 位十六进制作为一字。</summary>
        private static ushort ParseDataWord (string resp) {
            var sb = new System.Text.StringBuilder();
            foreach (char c in resp) {
                if (Uri.IsHexDigit(c))
                    sb.Append(c);
            }
            string hex = sb.ToString();
            if (hex.Length < 4)
                throw new Exception("无法解析数据字: " + resp);
            return ushort.Parse(hex.Substring(hex.Length - 4, 4), NumberStyles.HexNumber);
        }

        private static object ConvertWord (ushort word, VariableDataType t) {
            switch (t) {
                case VariableDataType.Int16:
                    return (short)word;
                case VariableDataType.Int32:
                    return (int)word;
                default:
                    return word;
            }
        }

        private static bool ToBool (object value) {
            if (value is bool b)
                return b;
            if (value == null)
                return false;

            string s = value.ToString().Trim();
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n != 0;

            return false;
        }

        private static ushort ToUInt16 (object value) {
            if (value == null)
                return 0;
            if (value is ushort u)
                return u;
            if (value is short sh)
                return (ushort)sh;
            if (value is int i)
                return (ushort)i;
            if (value is long l)
                return (ushort)l;

            double d;
            if (double.TryParse(
                    value.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out d))
                return (ushort)(int)d;

            return 0;
        }

        private static ProtocolDataMessage Fail (ProtocolDataMessage request, string message) {
            request.Success = false;
            request.Quality = DataQuality.Bad;
            request.ErrorMessage = message ?? "";
            return request;
        }

        public void Dispose () {
            if (_disposed)
                return;
            _disposed = true;
            _session.Dispose();
        }
    }
}