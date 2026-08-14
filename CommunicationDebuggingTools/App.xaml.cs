using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace CommunicationDebuggingTools {
    public partial class App : Application {
        private DispatcherTimer _heartbeat;

        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);
            MyAppServices.Initialize();

            // 每 3 秒在 UI 线程上检测各设备是否已断线
            // 使用 DispatcherTimer 而非 System.Threading.Timer：
            // 回调天然在 UI 线程执行，无跨线程问题
            _heartbeat = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(3)
            };
            _heartbeat.Tick += (s, ev) => MyAppServices.Devices?.CheckConnections();
            _heartbeat.Start();
        }

        protected override void OnExit (ExitEventArgs e) {
            _heartbeat?.Stop();
            try {
                MyAppServices.Devices?.DisconnectAll();
            } catch { }

            base.OnExit(e);
        }
    }
}