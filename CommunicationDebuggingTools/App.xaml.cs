using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core.Config;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.ViewModels;
using CommunicationDebuggingTools.Views.Pages.Device;
using CommunicationDebuggingTools.Views.Pages.Log;
using CommunicationDebuggingTools.Views.Pages.Monitor;
using CommunicationDebuggingTools.Views.Pages.Settings;
using CommunicationDebuggingTools.Views.VariableConfigPage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CommunicationDebuggingTools {

    /// <summary>
    /// 应用组合根（Composition Root）。
    /// 唯一知晓所有具体类型的地方；其他层只见接口。
    /// </summary>
    public partial class App : Application {

        /// <summary>根容器；退出 Dispose 后置 null，禁止再解析。</summary>
        public static IServiceProvider Services { get; private set; }

        private DispatcherTimer _heartbeat;
        private IDeviceService _deviceService;
        private IPollingEngine _pollingEngine;
        private IAppLogger _log;

        // ── 启动 ─────────────────────────────────────
        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);

            // 全局 UI 异常：记日志并阻止进程被直接干掉（便于继续排查）
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // ① 注册
            Services = BuildServiceProvider();

            _log = Services.GetRequiredService<IAppLogger>();
            _log.Info("App", "服务容器就绪");

            // ② 初始化（Load 与构造分离）；缓存单例，退出/心跳不再走已释放的 Services
            _deviceService = Services.GetRequiredService<IDeviceService>();
            _deviceService.Load();
            Services.GetRequiredService<IVariableService>().Load();

            // ③ 轮询引擎须在 UI 线程 Start（内部捕获 SynchronizationContext）
            _pollingEngine = Services.GetRequiredService<IPollingEngine>();
            _pollingEngine.Start();

            // ④ 心跳：只使用缓存引用，避免退出阶段访问已 Dispose 的 IServiceProvider
            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppConfig.HeartbeatIntervalSeconds) };
            _heartbeat.Tick += Heartbeat_Tick;
            _heartbeat.Start();

            // ⑤ 主窗口
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            _log.Info("App", "应用已启动");
        }

        private void App_DispatcherUnhandledException (
            object sender,
            DispatcherUnhandledExceptionEventArgs args) {
            try {
                _log?.Error("App", "UI 未处理异常", args.Exception);
            } catch { }

            try {
                MessageBox.Show(
                    args.Exception?.Message ?? "未知错误",
                    "程序异常",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            } catch { }

            args.Handled = true;
        }

        /// <summary>
        /// 心跳回调。Stop 之后仍可能有一次已排队的 Tick，必须容忍 ObjectDisposedException。
        /// </summary>
        private void Heartbeat_Tick (object sender, EventArgs e) {
            try {
                _deviceService?.CheckConnections();
            } catch (ObjectDisposedException) {
                // 退出过程中偶发
            } catch (Exception ex) {
                try { _log?.Error("App", "心跳异常", ex); } catch { }
            }
        }

        // ── 退出 ─────────────────────────────────────
        /// <summary>
        /// 顺序必须为：停心跳并摘回调 → 停轮询/断开 → 写日志 → Dispose 容器。
        /// 禁止在 Dispose 之后再 Services.GetService。
        /// </summary>
        protected override void OnExit (ExitEventArgs e) {
            // ① 先停心跳，移除回调，防止 Dispose 后仍触发 Tick
            if (_heartbeat != null) {
                try {
                    _heartbeat.Stop();
                    _heartbeat.Tick -= Heartbeat_Tick;
                } catch { }
                _heartbeat = null;
            }

            // ② 停业务（使用启动时缓存的引用）
            try { _pollingEngine?.Stop(); } catch { }
            try { _deviceService?.DisconnectAll(); } catch { }

            // ③ 日志必须在容器 Dispose 之前
            try { _log?.Info("App", "应用已退出"); } catch { }

            // ④ 释放根容器
            try {
                (Services as IDisposable)?.Dispose();
            } catch { }

            Services = null;
            _deviceService = null;
            _pollingEngine = null;
            _log = null;

            base.OnExit(e);
        }

        // ── 服务注册 ─────────────────────────────────
        private static IServiceProvider BuildServiceProvider () {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var sc = new ServiceCollection();

            // ── 基础设施 ──
            sc.AddSingleton<IAppLogger>(_ => new MemoryAppLogger(AppConfig.LogCapacity));

            // ── 协议解析器 ──
            sc.AddSingleton<IProtocolResolver>(sp => {
                string dir = Path.Combine(baseDir, "plugins");
                var resolver = new ProtocolResolver();
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

            // ── ViewModels（Transient）──
            sc.AddTransient<DevicePageViewModel>();
            sc.AddTransient<VariablePageViewModel>();
            sc.AddTransient<LogPageViewModel>();

            // ── Pages（Transient）──
            sc.AddTransient<DevicePage>();
            sc.AddTransient<VariableConfigPage>();
            sc.AddTransient<LogPage>();
            sc.AddTransient<DataMonitorPage>();
            sc.AddTransient<SettingsPage>();

            // ── 主窗口（Singleton）──
            sc.AddSingleton<MainWindow>();

            return sc.BuildServiceProvider();
        }
    }
}