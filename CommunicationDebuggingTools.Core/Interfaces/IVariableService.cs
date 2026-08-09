using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 变量配置与读写。设备连接由 <see cref="IDeviceService"/> 管理。
    /// </summary>
    public interface IVariableService {
        /// <summary>当前已加载的全部变量（可按 DeviceId 过滤使用）。</summary>
        ObservableCollection<VariableItem> Variables { get; }

        void Load ();
        void Save ();

        void Add (VariableItem item);
        void Update (VariableItem item);
        void Remove (string id);

        /// <summary>读取一点；校验 Access、设备已连接，经协议插件执行。</summary>
        Task<bool> ReadAsync (string variableId, CancellationToken cancellationToken);

        /// <summary>写入一点；value 写入报文，成功后更新 LastValue。</summary>
        Task<bool> WriteAsync (string variableId, object value, CancellationToken cancellationToken);

        /// <summary>按设备读取该设备下全部可读变量。</summary>
        Task ReadByDeviceAsync (string deviceId, CancellationToken cancellationToken);
    }
}