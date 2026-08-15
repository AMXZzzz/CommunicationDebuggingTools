using System.IO;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;

namespace CommunicationDebuggingTools.Services {
    /// <summary>
    /// 进程内全局服务：启动时 Initialize 一次，UI 通过静态属性访问。
    /// </summary>
    public static class MyAppServices {
        public static IDeviceService Devices { get; private set; }
        public static IProtocolResolver Protocols { get; private set; }
        public static IVariableService Variables { get; private set; }
        public static IPollingEngine Polling { get; private set; }
        public static IAppLogger Logger { get; private set; }

        public static void Initialize () {
            if (Devices != null) return;

            string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;

            Logger = new MemoryAppLogger(500);
            Logger.Info("App", "服务初始化开始");

            Protocols = CreateProtocolResolver(baseDir);
            Devices = CreateDeviceService(baseDir, Protocols);
            Variables = CreateVariableService(baseDir, Devices);
            Polling = new PollingEngine(Variables, Devices, Logger);  // 在 UI 线程构造

            Logger.Info("App", "服务初始化完成");
        }

        private static IDeviceService CreateDeviceService (
            string baseDir, IProtocolResolver resolver) {
            string configPath = Path.Combine(baseDir, "config", "devices.json");
            var service = new DeviceService(resolver, new JsonDeviceRepository(configPath), Logger);
            service.Load();
            return service;
        }

        private static IProtocolResolver CreateProtocolResolver (string baseDir) {
            string pluginDir = Path.Combine(baseDir, "plugins");
            var resolver = new ProtocolResolver();
            resolver.LoadFromFolder(pluginDir);

            var names = resolver.GetProtocolNames();
            int n = names != null ? names.Count : 0;
            Logger.Info("Protocol", "已加载协议 " + n + " 个，目录=" + pluginDir);
            return resolver;
        }

        private static IVariableService CreateVariableService (
            string baseDir, IDeviceService devices) {
            string path = Path.Combine(baseDir, "config", "variables.json");
            var service = new VariableService(devices, new JsonVariableRepository(path), Logger);
            service.Load();
            return service;
        }
    }
}