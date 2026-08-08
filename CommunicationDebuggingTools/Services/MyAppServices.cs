using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Core.Interfaces;
using System.IO;

namespace CommunicationDebuggingTools.Services {
    /// <summary>
    /// 进程内全局服务容器（简化版依赖注入/服务定位器）。
    /// 在应用启动时（App.xaml.cs 的 OnStartup）调用一次 Initialize 完成构建，
    /// 之后 UI 层各页面直接通过 Devices/Protocols 静态属性访问共享的业务服务实例。
    /// </summary>
    public static class MyAppServices {
        /// <summary>全局共享的设备业务服务实例，UI 页面统一通过它访问/操作设备。</summary>
        public static IDeviceService Devices { get; private set; }

        /// <summary>全局共享的协议插件解析器实例，可用于获取已加载的协议名称列表。</summary>
        public static IProtocolResolver Protocols { get; private set; }

        /// <summary>
        /// 初始化全局服务：加载 plugins 目录下的协议插件，构建 JSON 设备仓储与设备服务，
        /// 并从本地配置文件加载已保存的设备列表。重复调用时会直接返回（幂等），
        /// 避免多次初始化导致重复加载插件或重复读取配置文件。
        /// </summary>
        public static void Initialize () {
            if (Devices != null)
                return;

            string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
            string pluginDir = Path.Combine(baseDir, "plugins");
            string configPath = Path.Combine(baseDir, "config", "devices.json");

            var resolver = new ProtocolResolver();
            resolver.LoadFromFolder(pluginDir);

            var repo = new JsonDeviceRepository(configPath);
            var deviceService = new DeviceService(resolver, repo);
            deviceService.Load();

            Protocols = resolver;
            Devices = deviceService;
        }
    }
}