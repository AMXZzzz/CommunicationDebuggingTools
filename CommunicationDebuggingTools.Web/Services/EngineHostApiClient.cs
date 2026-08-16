using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Web.Models;

namespace CommunicationDebuggingTools.Web.Services;

public sealed class EngineHostApiClient {
    private readonly HttpClient _http;

    public EngineHostApiClient (HttpClient http) {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<EngineStatusDto?> GetStatusAsync (CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<EngineStatusDto>("/api/status", ct);

    public async Task<List<string>> GetProtocolsAsync (CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<string>>("/api/protocols", ct) ?? new List<string>();

    public async Task<List<DeviceDto>> GetDevicesAsync (CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<DeviceDto>>("/api/devices", ct) ?? new List<DeviceDto>();

    public async Task CreateDeviceAsync (DeviceUpsertRequest request, CancellationToken ct = default) {
        var resp = await _http.PostAsJsonAsync("/api/devices", request, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdateDeviceAsync (string id, DeviceUpsertRequest request, CancellationToken ct = default) {
        var resp = await _http.PutAsJsonAsync($"/api/devices/{id}", request, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteDeviceAsync (string id, CancellationToken ct = default) {
        var resp = await _http.DeleteAsync($"/api/devices/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<bool> ConnectDeviceAsync (string id, CancellationToken ct = default) {
        var resp = await _http.PostAsync($"/api/devices/{id}/connect", null, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ConnectResultDto>(cancellationToken: ct);
        return result?.Success == true;
    }

    public async Task DisconnectDeviceAsync (string id, CancellationToken ct = default) {
        var resp = await _http.PostAsync($"/api/devices/{id}/disconnect", null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<List<VariableDto>> GetVariablesAsync (CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<VariableDto>>("/api/variables", ct) ?? new List<VariableDto>();

    public async Task CreateVariableAsync (VariableUpsertRequest request, CancellationToken ct = default) {
        var resp = await _http.PostAsJsonAsync("/api/variables", request, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UpdateVariableAsync (string id, VariableUpsertRequest request, CancellationToken ct = default) {
        var resp = await _http.PutAsJsonAsync($"/api/variables/{id}", request, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteVariableAsync (string id, CancellationToken ct = default) {
        var resp = await _http.DeleteAsync($"/api/variables/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<VariableOpResultDto?> ReadVariableAsync (string id, CancellationToken ct = default) {
        var resp = await _http.PostAsync($"/api/variables/{id}/read", null, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<VariableOpResultDto>(cancellationToken: ct);
    }

    public async Task<VariableOpResultDto?> WriteVariableAsync (string id, string value, CancellationToken ct = default) {
        var resp = await _http.PostAsJsonAsync($"/api/variables/{id}/write", new VariableWriteRequest { Value = value }, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<VariableOpResultDto>(cancellationToken: ct);
    }

    public async Task<List<LogEntryDto>> GetLogsAsync (CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<LogEntryDto>>("/api/logs", ct) ?? new List<LogEntryDto>();
}
