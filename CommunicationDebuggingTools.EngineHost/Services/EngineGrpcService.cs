using System;
using System.Linq;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Contracts.V1;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using Grpc.Core;
using CoreOp = CommunicationDebuggingTools.Core.Enums.OperationResult;

namespace CommunicationDebuggingTools.EngineHost.Services {

    /// <summary>
    /// gRPC 引擎实现：设备 / 变量 CRUD、连接、读写。
    /// Watch* 流二期再补；一期返回空流结束。
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

        // ── 健康 / 元数据 ─────────────────────────────

        public override Task<HealthResponse> Health (HealthRequest request, ServerCallContext context) {
            int deviceCount = _devices?.Devices?.Count ?? 0;
            int variableCount = _variables?.Variables?.Count ?? 0;
            int connected = _devices?.Devices?.Count(d => d != null && d.IsConnected) ?? 0;

            return Task.FromResult(new HealthResponse {
                Ok = true,
                Version = "0.2.0-host",
                DeviceCount = deviceCount,
                VariableCount = variableCount,
                ConnectedDeviceCount = connected
            });
        }

        public override Task<ListProtocolsResponse> ListProtocols (
            ListProtocolsRequest request, ServerCallContext context) {
            var resp = new ListProtocolsResponse();
            foreach (string name in _protocols?.GetProtocolNames() ?? Array.Empty<string>()) {
                if (!string.IsNullOrWhiteSpace(name))
                    resp.ProtocolNames.Add(name);
            }
            return Task.FromResult(resp);
        }

        // ── 设备 ─────────────────────────────────────

        public override Task<ListDevicesResponse> ListDevices (
            ListDevicesRequest request, ServerCallContext context) {
            var resp = new ListDevicesResponse();
            if (_devices?.Devices == null)
                return Task.FromResult(resp);

            foreach (DeviceInfo d in _devices.Devices) {
                if (d == null) continue;
                resp.Devices.Add(ToDto(d));
            }
            return Task.FromResult(resp);
        }

        public override Task<UpsertDeviceResponse> UpsertDevice (
            UpsertDeviceRequest request, ServerCallContext context) {
            var resp = new UpsertDeviceResponse();
            if (request?.Device == null) {
                resp.Result = FailResult("device 为空", "INVALID_ARGUMENT");
                return Task.FromResult(resp);
            }

            try {
                DeviceInfo info = FromDto(request.Device);
                bool isNew = string.IsNullOrWhiteSpace(request.Device.Id);
                if (isNew) {
                    if (string.IsNullOrWhiteSpace(info.Id))
                        info.Id = Guid.NewGuid().ToString("N");
                    _devices.Add(info);
                } else {
                    _devices.Update(info);
                }

                DeviceInfo saved = _devices.Devices.FirstOrDefault(x => x != null && x.Id == info.Id);
                resp.Device = ToDto(saved ?? info);
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "UPSERT_FAILED");
            }
            return Task.FromResult(resp);
        }

        public override Task<DeleteDeviceResponse> DeleteDevice (
            DeleteDeviceRequest request, ServerCallContext context) {
            var resp = new DeleteDeviceResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return Task.FromResult(resp);
            }
            try {
                _devices.Remove(request.Id);
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "DELETE_FAILED");
            }
            return Task.FromResult(resp);
        }

        public override async Task<ConnectResponse> Connect (
            ConnectRequest request, ServerCallContext context) {
            var resp = new ConnectResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return resp;
            }
            try {
                bool ok = await _devices.ConnectAsync(request.Id, context.CancellationToken)
                    .ConfigureAwait(false);
                DeviceInfo d = FindDevice(request.Id);
                if (d != null) resp.Device = ToDto(d);
                resp.Result = ok
                    ? OkResult()
                    : FailResult(d?.StatusText ?? "连接失败", "CONNECT_FAILED");
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "CONNECT_FAILED");
                DeviceInfo d = FindDevice(request.Id);
                if (d != null) resp.Device = ToDto(d);
            }
            return resp;
        }

        public override Task<DisconnectResponse> Disconnect (
            DisconnectRequest request, ServerCallContext context) {
            var resp = new DisconnectResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return Task.FromResult(resp);
            }
            try {
                _devices.Disconnect(request.Id);
                DeviceInfo d = FindDevice(request.Id);
                if (d != null) resp.Device = ToDto(d);
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "DISCONNECT_FAILED");
            }
            return Task.FromResult(resp);
        }

        public override Task<DisconnectAllResponse> DisconnectAll (
            DisconnectAllRequest request, ServerCallContext context) {
            var resp = new DisconnectAllResponse();
            try {
                _devices.DisconnectAll();
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "DISCONNECT_ALL_FAILED");
            }
            return Task.FromResult(resp);
        }

        // ── 变量 ─────────────────────────────────────

        public override Task<ListVariablesResponse> ListVariables (
            ListVariablesRequest request, ServerCallContext context) {
            var resp = new ListVariablesResponse();
            if (_variables?.Variables == null)
                return Task.FromResult(resp);

            string deviceId = request?.DeviceId;
            foreach (VariableItem v in _variables.Variables) {
                if (v == null) continue;
                if (!string.IsNullOrEmpty(deviceId) &&
                    !string.Equals(v.DeviceId, deviceId, StringComparison.Ordinal))
                    continue;
                resp.Variables.Add(ToDto(v));
            }
            return Task.FromResult(resp);
        }

        public override Task<UpsertVariableResponse> UpsertVariable (
            UpsertVariableRequest request, ServerCallContext context) {
            var resp = new UpsertVariableResponse();
            if (request?.Variable == null) {
                resp.Result = FailResult("variable 为空", "INVALID_ARGUMENT");
                return Task.FromResult(resp);
            }
            try {
                VariableItem item = FromDto(request.Variable);
                bool isNew = string.IsNullOrWhiteSpace(request.Variable.Id);
                if (isNew) {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        item.Id = Guid.NewGuid().ToString("N");
                    _variables.Add(item);
                } else {
                    _variables.Update(item);
                }
                VariableItem saved = _variables.Variables.FirstOrDefault(x => x != null && x.Id == item.Id);
                resp.Variable = ToDto(saved ?? item);
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "UPSERT_FAILED");
            }
            return Task.FromResult(resp);
        }

        public override Task<DeleteVariableResponse> DeleteVariable (
            DeleteVariableRequest request, ServerCallContext context) {
            var resp = new DeleteVariableResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return Task.FromResult(resp);
            }
            try {
                _variables.Remove(request.Id);
                resp.Result = OkResult();
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "DELETE_FAILED");
            }
            return Task.FromResult(resp);
        }

        public override async Task<ReadVariableResponse> ReadVariable (
            ReadVariableRequest request, ServerCallContext context) {
            var resp = new ReadVariableResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return resp;
            }
            try {
                CoreOp op = await _variables.ReadAsync(request.Id, context.CancellationToken)
                    .ConfigureAwait(false);
                VariableItem v = FindVariable(request.Id);
                if (v != null) resp.Variable = ToDto(v);
                resp.Result = op != null && op.Success
                    ? OkResult()
                    : FailResult(op?.ErrorMessage ?? "读失败", op?.ErrorCode.ToString() ?? "READ_FAILED");
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "READ_FAILED");
            }
            return resp;
        }

        public override async Task<WriteVariableResponse> WriteVariable (
            WriteVariableRequest request, ServerCallContext context) {
            var resp = new WriteVariableResponse();
            if (string.IsNullOrWhiteSpace(request?.Id)) {
                resp.Result = FailResult("id 为空", "INVALID_ARGUMENT");
                return resp;
            }
            try {
                CoreOp op = await _variables.WriteAsync(
                        request.Id, request.Value ?? "", context.CancellationToken)
                    .ConfigureAwait(false);
                VariableItem v = FindVariable(request.Id);
                if (v != null) resp.Variable = ToDto(v);
                resp.Result = op != null && op.Success
                    ? OkResult()
                    : FailResult(op?.ErrorMessage ?? "写失败", op?.ErrorCode.ToString() ?? "WRITE_FAILED");
            } catch (Exception ex) {
                resp.Result = FailResult(ex.Message, "WRITE_FAILED");
            }
            return resp;
        }

        // ── Watch（一期空实现，避免客户端卡死：立刻完成）──

        public override async Task WatchDevices (
            Empty request,
            IServerStreamWriter<DeviceEvent> responseStream,
            ServerCallContext context) {
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public override async Task WatchVariables (
            Empty request,
            IServerStreamWriter<VariableValueEvent> responseStream,
            ServerCallContext context) {
            await Task.CompletedTask.ConfigureAwait(false);
        }

        // ── 映射 ─────────────────────────────────────

        private DeviceInfo FindDevice (string id) =>
            _devices?.Devices?.FirstOrDefault(x => x != null && x.Id == id);

        private VariableItem FindVariable (string id) =>
            _variables?.Variables?.FirstOrDefault(x => x != null && x.Id == id);

        private static DeviceDto ToDto (DeviceInfo d) {
            if (d == null) return new DeviceDto();
            return new DeviceDto {
                Id = d.Id ?? "",
                Name = d.Name ?? "",
                Model = d.Model ?? "",
                Protocol = d.Protocol ?? "",
                Ip = d.Ip ?? "",
                Port = d.Port,
                StationNo = d.StationNo,
                ExtraSettingsJson = string.IsNullOrWhiteSpace(d.ExtraSettingsJson) ? "{}" : d.ExtraSettingsJson,
                ByteOrder = d.ByteOrder.ToString(),
                WordOrder = d.WordOrder.ToString(),
                StringEncoding = d.StringEncoding.ToString(),
                Lane = d.Lane.ToString(),
                IsConnected = d.IsConnected,
                StatusType = d.StatusType.ToString(),
                StatusText = d.StatusText ?? ""
            };
        }

        private static DeviceInfo FromDto (DeviceDto dto) {
            var d = new DeviceInfo();
            if (!string.IsNullOrWhiteSpace(dto.Id))
                d.Id = dto.Id.Trim();
            d.Name = dto.Name ?? "";
            d.Model = dto.Model ?? "";
            d.Protocol = dto.Protocol ?? "";
            d.Ip = dto.Ip ?? "";
            d.Port = dto.Port > 0 ? dto.Port : 502;
            d.StationNo = dto.StationNo;
            d.ExtraSettingsJson = string.IsNullOrWhiteSpace(dto.ExtraSettingsJson) ? "{}" : dto.ExtraSettingsJson;

            LaneType lane;
            if (Enum.TryParse(dto.Lane, true, out lane)) d.Lane = lane;
            ByteOrder bo;
            if (Enum.TryParse(dto.ByteOrder, true, out bo)) d.ByteOrder = bo;
            WordOrder wo;
            if (Enum.TryParse(dto.WordOrder, true, out wo)) d.WordOrder = wo;
            StringEncodingKind se;
            if (Enum.TryParse(dto.StringEncoding, true, out se)) d.StringEncoding = se;
            return d;
        }

        private static VariableDto ToDto (VariableItem v) {
            if (v == null) return new VariableDto();
            return new VariableDto {
                Id = v.Id ?? "",
                DeviceId = v.DeviceId ?? "",
                Name = v.Name ?? "",
                Address = v.Address ?? "",
                DataType = v.DataType.ToString(),
                Access = v.Access.ToString(),
                Length = v.Length,
                Unit = v.Unit ?? "",
                Category = v.Category ?? "",
                Description = v.Description ?? "",
                LastValue = v.LastValue != null ? Convert.ToString(v.LastValue) : "",
                Quality = v.Quality.ToString(),
                LastError = v.LastError ?? ""
            };
        }

        private static VariableItem FromDto (VariableDto dto) {
            var v = new VariableItem();
            if (!string.IsNullOrWhiteSpace(dto.Id))
                v.Id = dto.Id.Trim();
            v.DeviceId = dto.DeviceId ?? "";
            v.Name = dto.Name ?? "";
            v.Address = dto.Address ?? "";
            v.Length = dto.Length;
            v.Unit = dto.Unit ?? "";
            v.Category = string.IsNullOrWhiteSpace(dto.Category) ? "状态点" : dto.Category;
            v.Description = dto.Description ?? "";

            VariableDataType dt;
            if (Enum.TryParse(dto.DataType, true, out dt)) v.DataType = dt;
            VariableAccess ac;
            if (Enum.TryParse(dto.Access, true, out ac)) v.Access = ac;
            return v;
        }

        private static Contracts.V1.OperationResult OkResult () =>
            new Contracts.V1.OperationResult { Ok = true, Message = "", ErrorCode = "" };

        private static Contracts.V1.OperationResult FailResult (string message, string code) =>
            new Contracts.V1.OperationResult {
                Ok = false,
                Message = message ?? "",
                ErrorCode = code ?? "UNKNOWN"
            };
    }
}
