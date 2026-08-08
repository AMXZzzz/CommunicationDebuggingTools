using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 设备信息（核心模型，UI / 业务 / 插件共用）
    /// </summary>
    public class DeviceInfo {
        /// <summary>唯一标识</summary>
        public string Id { get; set; }

        /// <summary>设备名称，如 上板机</summary>
        public string Name { get; set; }

        /// <summary>品牌 / 型号，如 Siemens S7-1500</summary>
        public string Model { get; set; }

        /// <summary>通讯协议</summary>
        public string Protocol { get; set; }

        /// <summary>IP 地址</summary>
        public string Ip { get; set; }

        /// <summary>端口</summary>
        public int Port { get; set; }

        /// <summary>站号 / Unit ID</summary>
        public int UnitId { get; set; }

        /// <summary>轨道类型</summary>
        public LaneType Lane { get; set; }

        /// <summary>状态类型（配色 + 逻辑）</summary>
        public DeviceStatusType StatusType { get; set; }

        /// <summary>是否已连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 状态显示文字（由 StatusType 推导，也可业务层写入）
        /// </summary>
        public string StatusText {
            get {
                switch (StatusType) {
                    case DeviceStatusType.Success:
                        return "RUN";
                    case DeviceStatusType.Connecting:
                        return "连接中...";
                    case DeviceStatusType.Warning:
                        return "警告";
                    case DeviceStatusType.Error:
                        return "ALARM";
                    default:
                        return "离线";
                }
            }
        }

        /// <summary>
        /// 是否双轨（兼容旧逻辑）<
        /// /summary>
        public bool IsDualLane {
            get { return Lane == LaneType.Dual; }
            set { Lane = value ? LaneType.Dual : LaneType.Single; }
        }


        /// <summary>
        /// 构造函数
        /// </summary>
        public DeviceInfo () {
            Id = System.Guid.NewGuid().ToString("N");
            Name = "新设备";
            Model = "";
            Protocol = "";
            Ip = "192.168.0.1";
            Port = 502;
            UnitId = 1;
            Lane = LaneType.Single;
            StatusType = DeviceStatusType.Offline;
            IsConnected = false;
        }
    }
}