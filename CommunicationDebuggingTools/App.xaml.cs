using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;
using System.Windows;

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
                MyAppServices.Devices?.DisconnectAll();
            } catch {
            }

            base.OnExit(e);
        }
    }
}