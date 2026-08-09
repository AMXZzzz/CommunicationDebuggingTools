using System;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace Plugin.SiemensS7 {
    /// <summary>
    /// Siemens S7 协议插件入口。
    /// 连接与地址解析已就绪；S7 PDU 读写在 Session 补全前返回明确错误。
    /// </summary>
    public sealed class SiemensS7Protocol : IProtocol, IProtocolDataAccess, IDisposable {
        private readonly SiemensS7Session  _session = new SiemensS7Session ();
        private bool _disposed;

        public bool IsConnected => _session.IsConnected;

        public string GetProtocolName () => "Siemens S7";

        /// <summary>建连：解析 rack/slot，TCP 连接（默认 102）。</summary>
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
                int port = context.Port > 0 ? context.Port : 102;
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

        /// <summary>读：先校验地址；PDU 未实现时返回失败说明。</summary>
        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException("request");

            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                // 校验地址语法（不合法直接失败）
                SiemensS7Session.ParseAddress(request.Address);

                // TODO: 按 DataType + S7Address 组 S7 Read Var
                return Task.FromResult(Fail(request, "S7 读 PDU 尚未实现"));
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        /// <summary>写：先校验地址；PDU 未实现时返回失败说明。</summary>
        public Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException("request");

            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();
                SiemensS7Session.ParseAddress(request.Address);

                return Task.FromResult(Fail(request, "S7 写 PDU 尚未实现"));
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        private static ProtocolDataMessage Fail (ProtocolDataMessage request, string message) {
            request.Success = false;
            request.Quality = DataQuality.Bad;
            request.ErrorMessage = message ?? "";
            return request;
        }

        public void Dispose () {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}