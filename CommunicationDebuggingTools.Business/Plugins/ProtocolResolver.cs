using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommunicationDebuggingTools.Core;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Business.Plugins {

    /// <summary>
    /// 协议插件解析器：扫描 Plugin.*.dll，通过反射读取 ProtocolNameAttribute 完成注册。
    ///
    /// 与旧版的区别：
    ///   旧版：Activator.CreateInstance 创建临时实例 → GetProtocolName() → 丢弃
    ///         问题：实例构造可能触发 I/O，临时实例未 Dispose（资源泄漏）。
    ///   新版：CustomAttributeData.GetCustomAttributes 直接读 Attribute，零实例化。
    /// </summary>
    public class ProtocolResolver : IProtocolResolver {

        private readonly Dictionary<string, Type> _map =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public void LoadFromFolder (string folder) {
            _map.Clear();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            foreach (string file in Directory.GetFiles(folder, "Plugin.*.dll")) {
                try { LoadAssembly(file); } catch { /* 单个插件失败不影响其他；日志由调用方决定 */ }
            }
        }

        public IProtocol Resolve (string protocolName) {
            if (string.IsNullOrWhiteSpace(protocolName)) return null;
            Type type;
            if (!_map.TryGetValue(protocolName.Trim(), out type)) return null;
            return Activator.CreateInstance(type) as IProtocol;
        }

        public IList<string> GetProtocolNames () =>
            new List<string>(_map.Keys);

        // ── 内部 ───────────────────────────────────────────
        private void LoadAssembly (string dllPath) {
            Assembly asm = Assembly.LoadFrom(dllPath);
            Type[] types;
            try {
                types = asm.GetTypes();
            } catch (ReflectionTypeLoadException ex) {
                types = ex.Types;
            }
            if (types == null) return;

            Type protocolInterface = typeof(IProtocol);
            Type attributeType    = typeof(ProtocolNameAttribute);

            foreach (Type t in types) {
                if (t == null || t.IsInterface || t.IsAbstract) continue;
                if (!protocolInterface.IsAssignableFrom(t)) continue;

                // 读 Attribute，不实例化
                ProtocolNameAttribute attr =
                    (ProtocolNameAttribute)Attribute.GetCustomAttribute(t, attributeType);

                if (attr == null || string.IsNullOrWhiteSpace(attr.Name)) continue;

                _map[attr.Name] = t;    // 同名后者覆盖
            }
        }
    }
}