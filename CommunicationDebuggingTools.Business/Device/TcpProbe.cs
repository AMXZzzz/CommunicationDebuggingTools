using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicationDebuggingTools.Business.Tools {
    /// <summary>
    /// TCP 端口连通性探测（连接前预检）
    /// </summary>
    public static class TcpProbe {
        public static async Task<bool> IsPortOpenAsync (
            string ip,
            int port,
            int timeoutMs,
            CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(ip) || port <= 0)
                return false;

            try {
                using (var client = new TcpClient()) {
                    Task connectTask = client.ConnectAsync(ip, port);
                    Task delayTask = Task.Delay(timeoutMs, cancellationToken);

                    Task finished = await Task.WhenAny(connectTask, delayTask)
                        .ConfigureAwait(false);

                    if (finished != connectTask)
                        return false;

                    await connectTask.ConfigureAwait(false);
                    return client.Connected;
                }
            } catch {
                return false;
            }
        }
    }
}