using CommunicationDebuggingTools.Services;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CommunicationDebuggingTools {
    /// <summary>
    /// App.xaml 的交互逻辑（应用程序入口）。
    /// </summary>
    public partial class App : Application {

        /// <summary>
        /// 应用程序启动时回调：在任何 UI 窗口创建之前先完成全局服务（设备服务、协议插件）的初始化，
        /// 保证 MainWindow 及各页面在加载时可以直接使用 MyAppServices.Devices 等共享实例。
        /// </summary>
        protected override void OnStartup (StartupEventArgs e) {
            base.OnStartup(e);
            MyAppServices.Initialize();

        }


    }
}
