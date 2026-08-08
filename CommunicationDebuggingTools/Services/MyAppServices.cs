using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Core.Interfaces;
using System.IO;

namespace CommunicationDebuggingTools.Services {
    /// <summary>
    /// 进程内服务组装，只在启动时调用一次
    /// </summary>
    public static class MyAppServices {
        public static IDeviceService Devices { get; private set; }
        public static IProtocolResolver Protocols { get; private set; }

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