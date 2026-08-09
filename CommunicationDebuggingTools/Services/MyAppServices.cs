using System.IO;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Persistence;
using CommunicationDebuggingTools.Business.Tools;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Services {
    /// <summary>
    /// 进程内全局服务容器（简化服务定位器）。
    /// 在 App 启动时调用一次 <see cref="Initialize"/>，UI 通过静态属性访问。
    /// </summary>
    public static class MyAppServices {
        /// <summary>设备业务服务。</summary>
        public static IDeviceService Devices { get; private set; }

        /// <summary>协议插件解析器。</summary>
        public static IProtocolResolver Protocols { get; private set; }

        /// <summary>变量配置与读写。</summary>
        public static IVariableService Variables { get; private set; }

        /// <summary>
        /// 幂等初始化：协议 → 设备 → 变量。
        /// 已初始化则直接返回。
        /// </summary>
        public static void Initialize () {
            if (Devices != null)
                return;

            string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;

            Protocols = CreateProtocolResolver(baseDir);
            Devices = CreateDeviceService(baseDir, Protocols);
            Variables = CreateVariableService(baseDir,Devices);
        }

        /// <summary>加载 plugins 目录下的协议 DLL。</summary>
        private static IProtocolResolver CreateProtocolResolver (string baseDir) {
            string pluginDir = Path.Combine(baseDir, "plugins");
            var resolver = new ProtocolResolver();
            resolver.LoadFromFolder(pluginDir);
            return resolver;
        }

        /// <summary>构建设备服务并加载 devices.json。</summary>
        private static IDeviceService CreateDeviceService (
            string baseDir, IProtocolResolver resolver) {
            string configPath = Path.Combine(baseDir, "config", "devices.json");
            var repo = new JsonDeviceRepository(configPath);
            var service = new DeviceService(resolver, repo);
            service.Load();
            return service;
        }


        /// <summary>构建变量服务并加载 config\variables.json。</summary>
        private static IVariableService CreateVariableService (string baseDir, IDeviceService devices) {
            string path = Path.Combine(baseDir, "config", "variables.json");
            var service = new VariableService(devices, new JsonVariableRepository(path));
            service.Load();
            return service;
        }
    }
}