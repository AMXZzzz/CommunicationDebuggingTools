using CommunicationDebuggingTools.Core.Attributes;
using CommunicationDebuggingTools.Core.Config;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Panasonic {
    /// <summary>
    /// 松下 MEWTOCOL-COM 协议插件（真机规范）。
    /// 地址/站号/报文仅在本插件与 Session 内处理；编解码见 <see cref="Tools"/>。
    /// <para>
    /// 数据区 RD/WD：区码 + 5 位十进制，起止各一次（如 D00100D00101）。
    /// 字内线上低字节在前；字间顺序由 <see cref="ProtocolDataMessage.WordOrder"/> 决定；
    /// 字符串编码由 <see cref="ProtocolDataMessage.StringEncoding"/> 决定。
    /// </para>
    /// </summary>
    [ProtocolName("Panasonic MEWTOCOL")]
    public sealed class PanasonicProtocol : IProtocol {
        private readonly PanasonicSession _session = new PanasonicSession();
        private bool _disposed;

        public bool IsConnected => _session.IsConnected;

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

        public void Disconnect () => _session.Disconnect();

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

                if (addr.IsBit) {
                    string cmd = "RCS" + PanasonicSession.FormatContact(addr);
                    string resp = _session.Transact(cmd);
                    EnsureNoError(resp);
                    request.Value = ParseContactValue(resp);
                    request.Success = true;
                    request.Quality = DataQuality.Good;
                    request.ErrorMessage = "";
                    return Task.FromResult(request);
                }

                int wordCount = Tools.WordsNeeded(
                    request.DataType, request.Length, request.StringEncoding);
                string rdResp = _session.Transact("RD" + FormatDataRange(addr, wordCount));
                EnsureNoError(rdResp);

                ushort[] data = ParseDataWords(rdResp, wordCount);
                request.Value = Tools.FromWords(
                    data,
                    request.DataType,
                    request.WordOrder,
                    request.ByteOrder,
                    request.Length,
                    request.StringEncoding);
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

                if (addr.IsBit) {
                    bool bit = Tools.ToBool(request.Value);
                    string cmd = "WCS" + PanasonicSession.FormatContact(addr) + (bit ? "1" : "0");
                    string resp = _session.Transact(cmd);
                    EnsureNoError(resp);
                    request.Success = true;
                    request.Quality = DataQuality.Good;
                    request.ErrorMessage = "";
                    return Task.FromResult(request);
                }

                ushort[] words = Tools.ToWords(
                    request.Value,
                    request.DataType,
                    request.Length,
                    request.WordOrder,
                    request.ByteOrder,
                    request.StringEncoding);

                var sb = new System.Text.StringBuilder();
                sb.Append("WD").Append(FormatDataRange(addr, words.Length));
                for (int i = 0; i < words.Length; i++)
                    sb.Append(Tools.SwapBytes(words[i]).ToString("X4"));

                string wdResp = _session.Transact(sb.ToString());
                EnsureNoError(wdResp);

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

        public Task<bool> PingAsync (CancellationToken cancellationToken) {
            if (!IsConnected)
                return Task.FromResult(false);
            try {
                cancellationToken.ThrowIfCancellationRequested();
                string resp = _session.Transact("RDD00000D00000");
                return Task.FromResult(
                    !string.IsNullOrEmpty(resp) && resp.IndexOf('!') < 0);
            } catch {
                return Task.FromResult(false);
            }
        }

        /// <summary>真机范围：D00100D00101。</summary>
        private static string FormatDataRange (PanasonicAddress addr, int wordCount) {
            if (wordCount < 1)
                wordCount = 1;
            char code = addr.Area == PanasonicArea.WR ? 'W' : 'D';
            int start = addr.Index;
            int end = start + wordCount - 1;
            return code + start.ToString("D5") + code + end.ToString("D5");
        }

        private static void EnsureNoError (string resp) {
            if (string.IsNullOrEmpty(resp))
                throw new Exception("空响应");
            int bang = resp.IndexOf('!');
            if (bang >= 0) {
                string code = resp.Length >= bang + 5
                    ? resp.Substring(bang, 5)
                    : resp.Substring(bang);
                throw new Exception("MEWTOCOL 错误: " + code);
            }
        }

        private static bool ParseContactValue (string resp) {
            int i = resp.IndexOf("$RC", StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                throw new Exception("触点响应无效: " + resp);
            int p = i + 3;
            while (p < resp.Length && (resp[p] == ' ' || resp[p] == '\r'))
                p++;
            if (p >= resp.Length)
                throw new Exception("触点响应无数据");
            return resp[p] == '1';
        }

        private static ushort[] ParseDataWords (string resp, int wordCount) {
            int idx = resp.IndexOf("$RD", StringComparison.OrdinalIgnoreCase);
            string data = idx >= 0 ? resp.Substring(idx + 3) : resp;

            var hex = new System.Text.StringBuilder();
            for (int i = 0; i < data.Length; i++) {
                char c = data[i];
                if ((c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'F') ||
                    (c >= 'a' && c <= 'f'))
                    hex.Append(c);
            }

            string h = hex.ToString();
            int need = wordCount * 4;
            if (h.Length >= need + 2)
                h = h.Substring(0, need);
            if (h.Length < need)
                throw new Exception("数据字不足: " + resp);

            ushort[] words = new ushort[wordCount];
            for (int i = 0; i < wordCount; i++) {
                ushort raw = ushort.Parse(
                    h.Substring(i * 4, 4),
                    System.Globalization.NumberStyles.HexNumber);
                words[i] = Tools.SwapBytes(raw);
            }
            return words;
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