# EngineHost

C# 引擎进程：内部使用 Business / Core / Plugin，对外只提供 **gRPC**。

## 运行

```bash
# 先编译三个 Plugin 项目
dotnet build ../Plugin.ModbusTcp -c Debug
dotnet build ../Plugin.Panasonic -c Debug
dotnet build ../Plugin.SiemensS7 -c Debug
dotnet run --project CommunicationDebuggingTools.EngineHost.csproj
```

默认监听：`http://127.0.0.1:5100`（HTTP/2）

## 已实现 RPC

- `Health`
- `ListProtocols`

设备 / 变量 CRUD 与读写、Watch 流：下一步。
