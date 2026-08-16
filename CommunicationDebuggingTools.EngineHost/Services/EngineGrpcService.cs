using System.Linq;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Contracts.V1;
using CommunicationDebuggingTools.Core.Interfaces;
using Grpc.Core;

namespace CommunicationDebuggingTools.EngineHost.Services {

    /// <summary>
    /// gRPC 引擎实现（一期仅 Health；设备/变量 RPC 下一步补全）。
    /// 内部只依赖 Business 接口，不解析协议细节。
    /// </summary>
    public sealed class EngineGrpcService : Engine.EngineBase {
        private readonly IDeviceService _devices;
        private readonly IVariableService _variables;
        private readonly IProtocolResolver _protocols;

        public EngineGrpcService (
            IDeviceService devices,
            IVariableService variables,
            IProtocolResolver protocols) {
            _devices = devices;
            _variables = variables;
            _protocols = protocols;
        }

        public override Task<HealthResponse> Health (HealthRequest request, ServerCallContext context) {
            int deviceCount = _devices?.Devices?.Count ?? 0;
            int variableCount = _variables?.Variables?.Count ?? 0;
            int connected = 0;
            if (_devices?.Devices != null) {
                connected = _devices.Devices.Count(d => d != null && d.IsConnected);
            }

            return Task.FromResult(new HealthResponse {
                Ok = true,
                Version = "0.1.0-host",
                DeviceCount = deviceCount,
                VariableCount = variableCount,
                ConnectedDeviceCount = connected
            });
        }

        public override Task<ListProtocolsResponse> ListProtocols (
            ListProtocolsRequest request, ServerCallContext context) {
            var resp = new ListProtocolsResponse();
            if (_protocols != null) {
                foreach (string name in _protocols.GetProtocolNames() ?? System.Array.Empty<string>()) {
                    if (!string.IsNullOrWhiteSpace(name))
                        resp.ProtocolNames.Add(name);
                }
            }
            return Task.FromResult(resp);
        }
    }
}
