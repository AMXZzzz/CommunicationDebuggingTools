using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommunicationDebuggingTools.Core.Interfaces;

namespace CommunicationDebuggingTools.Business.Tools {
    /// <summary>
    /// 协议插件解析器的默认实现：扫描指定目录下命名为 Plugin.*.dll 的程序集，
    /// 反射查找其中实现了 <see cref="IProtocol"/> 接口的类型并注册到内部映射表，
    /// 之后按协议名称（如 "ModbusTcp"）创建新的协议实例供设备连接使用。
    /// </summary>
    public class ProtocolResolver : IProtocolResolver {
        /// <summary>
        /// 协议名称 → 实现类型 的映射（大小写不敏感）。
        /// 只保存类型信息，不缓存实例：每次 Resolve 都通过 Activator 创建新实例，
        /// 避免多个设备共用同一个协议对象而互相干扰连接状态。
        /// </summary>
        private readonly Dictionary<string, Type> _map =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 从指定目录加载所有 Plugin.*.dll 插件程序集，并重建协议名称映射表。
        /// 每次调用都会清空旧的映射，因此可用于插件热重载场景。
        /// 单个插件程序集加载失败不会影响其余插件的加载。
        /// </summary>
        /// <param name="folder">插件所在目录；为空或不存在时直接返回，映射表将为空。</param>
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

        /// <summary>
        /// 根据协议名称创建一个新的协议实例。
        /// </summary>
        /// <param name="protocolName">协议显示名称（大小写不敏感），如 "ModbusTcp"。</param>
        /// <returns>新创建的协议实例；名称为空或未找到匹配插件时返回 null。</returns>
        public IProtocol Resolve (string protocolName) {
            if (string.IsNullOrWhiteSpace(protocolName))
                return null;

            Type type;
            if (!_map.TryGetValue(protocolName.Trim(), out type))
                return null;

            return Activator.CreateInstance(type) as IProtocol;
        }

        /// <summary>
        /// 获取当前已加载的全部协议名称列表。
        /// </summary>
        public IList<string> GetProtocolNames () {
            return new List<string>(_map.Keys);
        }

        /// <summary>
        /// 加载单个插件程序集：反射枚举其中的公开类型，
        /// 筛选出未被抽象/接口修饰且实现了 <see cref="IProtocol"/> 的类型，
        /// 通过创建一个临时实例调用 GetProtocolName() 获取协议名称并登记到映射表中。
        /// 若程序集中部分类型无法加载（<see cref="ReflectionTypeLoadException"/>），
        /// 会退化为使用异常中已成功加载的类型集合，而不是整体失败。
        /// </summary>
        /// <param name="dllPath">插件 DLL 的完整路径。</param>
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