using CommunicationDebuggingTools.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace CommunicationDebuggingTools {
    public partial class App : Application {

        private DispatcherTimer _heartbeat;

        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);

            // 必须在 UI 线程初始化，PollingEngine/DeviceService 需要捕获
            // SynchronizationContext.Current（WPF DispatcherSynchronizationContext）
            MyAppServices.Initialize();

            // ── 心跳：每 3 秒检测 TCP 层断线 ──
            _heartbeat = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(3)
            };
            _heartbeat.Tick += (s, ev) => MyAppServices.Devices?.CheckConnections();
            _heartbeat.Start();

            // ── 启动变量轮询引擎 ──
            MyAppServices.Polling?.Start();

            MyAppServices.Logger?.Info("App", "应用已启动");
        }

        protected override void OnExit (ExitEventArgs e) {
            // 按依赖顺序逆向释放
            try { MyAppServices.Polling?.Stop(); } catch { }
            try { _heartbeat?.Stop(); } catch { }
            try { MyAppServices.Devices?.DisconnectAll(); } catch { }

            MyAppServices.Logger?.Info("App", "应用已退出");
            base.OnExit(e);
        }
    }
}