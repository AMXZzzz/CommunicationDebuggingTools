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

            //! 匿名方法中使用 MyAppServices.Devices?.CheckConnections()，
            //而非直接使用 MyAppServices.Devices.CheckConnections()，
            //是为了避免在 MyAppServices.Devices 为 null 时抛出 NullReferenceException 异常。
            //通过使用 null 条件运算符（?.），当 MyAppServices.Devices 为 null 时，整个表达式将返回 null，
            //而不会调用 CheckConnections() 方法，从而避免了异常的发生。
            _heartbeat.Tick +=          (s, ev) => MyAppServices.Devices?.CheckConnections();
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