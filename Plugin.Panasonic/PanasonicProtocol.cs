using System;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;

namespace Plugin.Panasonic {
    /// <summary>
    /// 松下 MEWTOCOL 协议插件入口。
    /// 地址与站号已就绪；读写报文在 Session 补全前返回明确错误。
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
                PanasonicSession.ParseAddress(request.Address);
                return Task.FromResult(Fail(request, "MEWTOCOL 读报文尚未实现"));
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
                PanasonicSession.ParseAddress(request.Address);
                return Task.FromResult(Fail(request, "MEWTOCOL 写报文尚未实现"));
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