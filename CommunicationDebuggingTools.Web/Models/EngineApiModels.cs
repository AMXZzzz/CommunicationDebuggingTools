using System;

namespace CommunicationDebuggingTools.Web.Models;

public sealed class EngineStatusDto {
    public DateTimeOffset ServerTime { get; set; }
    public int DeviceCount { get; set; }
    public int ConnectedDeviceCount { get; set; }
    public int VariableCount { get; set; }
}

public sealed class DeviceDto {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public int StationNo { get; set; }
    public string Lane { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class VariableDto {
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Access { get; set; } = string.Empty;
    public int Length { get; set; }
    public string? Value { get; set; }
    public string Quality { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class LogEntryDto {
    public DateTime Time { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class DeviceUpsertRequest {
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public int StationNo { get; set; } = 1;
    public string Lane { get; set; } = string.Empty;
}

public sealed class VariableUpsertRequest {
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Int16";
    public string Access { get; set; } = "ReadWrite";
    public int Length { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class VariableWriteRequest {
    public string Value { get; set; } = string.Empty;
}

public sealed class VariableOpResultDto {
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class ConnectResultDto {
    public bool Success { get; set; }
}
