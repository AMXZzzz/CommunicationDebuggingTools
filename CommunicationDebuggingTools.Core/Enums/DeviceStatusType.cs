using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommunicationDebuggingTools.Core.Enums {
    /// <summary>状态类型（决定 UI 颜色）</summary>
    public enum DeviceStatusType {
        Offline = 0,
        Connecting = 1,
        Success = 2,   // RUN
        Warning = 3,
        Error = 4      // ALARM / 超时
    }
}