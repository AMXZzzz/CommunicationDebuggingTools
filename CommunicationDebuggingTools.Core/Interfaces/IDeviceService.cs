using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.ObjectModel;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 设备业务服务：UI 只依赖此接口
    /// </summary>
    public interface IDeviceService {
        ObservableCollection<DeviceInfo> Devices { get; }

        void Load ();
        void Save ();

        void Add (DeviceInfo device);
        void Update (DeviceInfo device);
        void Remove (string id);

        bool Connect (string id);
        void Disconnect (string id);

        /// <summary>获取该设备当前协议实例（未连接可为 null）</summary>
        IProtocol GetProtocol (string deviceId);
    }
}