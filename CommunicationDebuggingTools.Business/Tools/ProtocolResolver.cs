using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Business.Tools {
    /// <summary>
    /// 扫描 plugins 目录，加载实现了 IProtocol 的插件
    /// </summary>
    public class ProtocolResolver : IProtocolResolver {
        // 协议名 → 实现类型（每次 Resolve 再 Activator 创建，避免多设备共实例）
        private readonly Dictionary<string, Type> _map =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public void LoadFromFolder (string folder) {
            _map.Clear();

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            string[] files = Directory.GetFiles(folder, "Plugin.*.dll");
            foreach (string file in files) {
                try {
                    LoadAssembly(file);
                } catch {
                    // 单个插件失败不影响其它；正式环境可写日志
                }
            }
        }

        public IProtocol Resolve (string protocolName) {
            if (string.IsNullOrWhiteSpace(protocolName))
                return null;

            Type type;
            if (!_map.TryGetValue(protocolName.Trim(), out type))
                return null;

            return Activator.CreateInstance(type) as IProtocol;
        }

        public IList<string> GetProtocolNames () {
            return new List<string>(_map.Keys);
        }

        private void LoadAssembly (string dllPath) {
            Assembly asm = Assembly.LoadFrom(dllPath);
            Type[] types;
            try {
                types = asm.GetTypes();
            } catch (ReflectionTypeLoadException ex) {
                types = ex.Types;
            }

            if (types == null)
                return;

            Type protocolInterface = typeof(IProtocol);

            foreach (Type t in types) {
                if (t == null || t.IsInterface || t.IsAbstract)
                    continue;
                if (!protocolInterface.IsAssignableFrom(t))
                    continue;

                IProtocol sample = null;
                try {
                    sample = Activator.CreateInstance(t) as IProtocol;
                } catch {
                    continue;
                }

                if (sample == null)
                    continue;

                string name = sample.GetProtocolName();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // 同名后者覆盖
                _map[name.Trim()] = t;
            }
        }
    }
}