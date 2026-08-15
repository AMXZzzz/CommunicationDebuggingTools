using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.ViewModels;
using CommunicationDebuggingTools.Views.Pages.Device;
using CommunicationDebuggingTools.Views.Pages.Log;
using CommunicationDebuggingTools.Views.Pages.Monitor;
using CommunicationDebuggingTools.Views.VariableConfigPage;
using CommunicationDebuggingTools.Views.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace CommunicationDebuggingTools {

    /// <summary>
    /// 应用组合根（Composition Root）。
    /// 唯一知晓所有具体类型的地方；其他层只见接口。
    /// 不再有 MyAppServices——所有服务通过构造注入或 ServiceProvider 显式解析。
    /// </summary>
    public partial class App : Application {

        public static IServiceProvider Services { get; private set; }

        private DispatcherTimer _heartbeat;

        // ── 启动 ─────────────────────────────────────
        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);

            // ① 注册
            Services = BuildServiceProvider();

            IAppLogger log = Services.GetRequiredService<IAppLogger>();
            log.Info("App", "服务容器就绪");

            // ② 初始化（Load 分离于构造）
            Services.GetRequiredService<IDeviceService>().Load();
            Services.GetRequiredService<IVariableService>().Load();

            // ③ 轮询引擎在 UI 线程启动（捕获 SynchronizationContext）
            Services.GetRequiredService<IPollingEngine>().Start();

            // ④ 心跳
            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _heartbeat.Tick += (_, __) =>
                Services.GetRequiredService<IDeviceService>().CheckConnections();
            _heartbeat.Start();

            // ⑤ 主窗口（由 DI 创建，MainWindow 构造注入 IServiceProvider）
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            log.Info("App", "应用已启动");
        }

        // ── 退出 ─────────────────────────────────────
        protected override void OnExit (ExitEventArgs e) {
            try { Services?.GetService<IPollingEngine>()?.Stop(); } catch { }
            try { _heartbeat?.Stop(); } catch { }
            try { Services?.GetService<IDeviceService>()?.DisconnectAll(); } catch { }
            try { (Services as IDisposable)?.Dispose(); } catch { }

            Services?.GetService<IAppLogger>()?.Info("App", "应用已退出");
            base.OnExit(e);
        }

        // ── 服务注册 ─────────────────────────────────
        private static IServiceProvider BuildServiceProvider () {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var sc = new ServiceCollection();

            // ── 基础设施 ──
            sc.AddSingleton<IAppLogger>(_ => new MemoryAppLogger(500));

            // ── 协议解析器 ──
            sc.AddSingleton<IProtocolResolver>(sp => {
                string dir    = Path.Combine(baseDir, "plugins");
                var resolver  = new ProtocolResolver();
                resolver.LoadFromFolder(dir);
                int n = resolver.GetProtocolNames()?.Count ?? 0;
                sp.GetRequiredService<IAppLogger>()
                  .Info("Protocol", "已加载协议 " + n + " 个，目录=" + dir);
                return resolver;
            });

            // ── 持久化 ──
            sc.AddSingleton<IDeviceRepository>(_ =>
                new JsonDeviceRepository(
                    Path.Combine(baseDir, "config", "devices.json")));
            sc.AddSingleton<IVariableRepository>(_ =>
                new JsonVariableRepository(
                    Path.Combine(baseDir, "config", "variables.json")));

            // ── 业务服务（Singleton）──
            sc.AddSingleton<IDeviceService, DeviceService>();
            sc.AddSingleton<IVariableService, VariableService>();
            sc.AddSingleton<IPollingEngine, PollingEngine>();

            // ── ViewModels（Transient：每次导航创建新实例）──
            sc.AddTransient<DevicePageViewModel>();
            sc.AddTransient<VariablePageViewModel>();
            sc.AddTransient<LogPageViewModel>();

            // ── Pages（Transient：依赖 Transient ViewModel）──
            sc.AddTransient<DevicePage>();
            sc.AddTransient<VariableConfigPage>();
            sc.AddTransient<LogPage>();
            sc.AddTransient<DataMonitorPage>();
            sc.AddTransient<SettingsPage>(); 

            // ── 主窗口（Singleton：只有一个）──
            sc.AddSingleton<MainWindow>();

            return sc.BuildServiceProvider();
        }
    }
}