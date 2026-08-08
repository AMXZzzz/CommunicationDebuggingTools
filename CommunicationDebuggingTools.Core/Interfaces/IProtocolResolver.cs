using System.Collections.Generic;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 协议插件解析器：加载 plugins 目录，按名称创建实例
    /// </summary>
    public interface IProtocolResolver {
        /// <summary>从目录加载 Plugin.*.dll</summary>
        void LoadFromFolder (string folder);

        /// <summary>按协议显示名获取新实例；找不到返回 null</summary>
        IProtocol Resolve (string protocolName);

        /// <summary>已加载的协议名称列表</summary>
        IList<string> GetProtocolNames ();
    }
}