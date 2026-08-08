using System.Windows;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools {
    public partial class App : Application {
        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);
            MyAppServices.Initialize();
        }

        /// <summary>
        /// 应用退出：断开所有设备会话，释放 TCP。
        /// </summary>
        protected override void OnExit (ExitEventArgs e) {
            try {
                MyAppServices.Devices.DisconnectAll();
            } catch {
                // 退出时不再抛出
            }

            base.OnExit(e);
        }
    }
}