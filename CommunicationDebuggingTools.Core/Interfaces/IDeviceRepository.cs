using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {
    /// <summary>
    /// 设备配置持久化（JSON / 数据库等由 Infrastructure 实现）
    /// </summary>
    public interface IDeviceRepository {
        IList<DeviceInfo> LoadAll ();
        void SaveAll (IList<DeviceInfo> devices);
    }
}