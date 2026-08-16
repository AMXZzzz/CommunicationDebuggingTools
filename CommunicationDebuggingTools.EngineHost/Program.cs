using System;
using System.IO;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Plugins;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.EngineHost.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommunicationDebuggingTools.EngineHost {

    /// <summary>
    /// 引擎进程入口：加载 Business + 插件，对外仅暴露 gRPC（默认 http://127.0.0.1:5100）。
    /// WPF 暂仍可进程内调用 Business；多端客户端只连本 Host。
    /// </summary>
    public static class Program {

        public static void Main (string[] args) {
            string baseDir = AppContext.BaseDirectory;

            var builder = WebApplication.CreateBuilder(args);

            // 强制 HTTP/2 明文（本机开发）；生产可再加 HTTPS
            builder.WebHost.ConfigureKestrel(options => {
                options.ListenLocalhost(5100, o => o.Protocols = HttpProtocols.Http2);
            });

            builder.Services.AddGrpc();
            RegisterBusiness(builder.Services, baseDir);

            var app = builder.Build();

            // 启动时加载配置与轮询
            var log = app.Services.GetRequiredService<IAppLogger>();
            try {
                app.Services.GetRequiredService<IDeviceService>().Load();
                app.Services.GetRequiredService<IVariableService>().Load();
                app.Services.GetRequiredService<IPollingEngine>().Start();
                log.Info("EngineHost", "设备/变量已加载，轮询已启动");
            } catch (Exception ex) {
                log.Error("EngineHost", "启动加载失败: " + ex.Message, ex);
            }

            app.MapGrpcService<EngineGrpcService>();
            app.MapGet("/", () =>
                "CommunicationDebuggingTools EngineHost — gRPC at http://127.0.0.1:5100");

            log.Info("EngineHost", "gRPC 监听 http://127.0.0.1:5100");
            app.Run();
        }

        /// <summary>与 WPF App 对齐的业务注册（不含 UI）。</summary>
        private static void RegisterBusiness (IServiceCollection sc, string baseDir) {
            sc.AddSingleton<IAppLogger>(_ => new MemoryAppLogger(AppConfig.LogCapacity));

            sc.AddSingleton<IProtocolResolver>(sp => {
                string dir = Path.Combine(baseDir, "plugins");
                var log = sp.GetRequiredService<IAppLogger>();
                var resolver = new ProtocolResolver(log);
                resolver.LoadFromFolder(dir);
                int n = resolver.GetProtocolNames()?.Count ?? 0;
                log.Info("Protocol", "已加载协议 " + n + " 个，目录=" + dir);
                return resolver;
            });

            sc.AddSingleton<IDeviceRepository>(_ =>
                new JsonDeviceRepository(Path.Combine(baseDir, "config", "devices.json")));
            sc.AddSingleton<IVariableRepository>(_ =>
                new JsonVariableRepository(Path.Combine(baseDir, "config", "variables.json")));

            sc.AddSingleton<ITcpProbe, TcpProbe>();
            sc.AddSingleton<IDeviceService, DeviceService>();
            sc.AddSingleton<IVariableService, VariableService>();
            sc.AddSingleton<IPollingEngine, PollingEngine>();
        }
    }
}
