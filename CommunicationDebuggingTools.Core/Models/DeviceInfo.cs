using System.ComponentModel;
using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 设备信息模型（UI / 业务 / 持久化共用）。
    /// 实现 <see cref="INotifyPropertyChanged"/>，属性变更后绑定控件与卡片状态可自动刷新，
    /// 无需整表重建设备列表。
    /// </summary>
    public class DeviceInfo : INotifyPropertyChanged {
        /// <summary>属性变更通知（WPF 绑定订阅）。</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private string _id;
        private string _name;
        private string _model;
        private string _protocol;
        private string _ip;
        private int _port;
        private int _unitId;
        private LaneType _lane;
        private DeviceStatusType _statusType;
        private bool _isConnected;

        /// <summary>唯一标识（新增时由构造函数生成，持久化后保持不变）。</summary>
        public string Id {
            get { return _id; }
            set { if (_id == value) return; _id = value; Raise("Id"); }
        }

        /// <summary>设备显示名称，如「上板机」。</summary>
        public string Name {
            get { return _name; }
            set { if (_name == value) return; _name = value; Raise("Name"); }
        }

        /// <summary>品牌 / 型号，如「S7-1500」。</summary>
        public string Model {
            get { return _model; }
            set { if (_model == value) return; _model = value; Raise("Model"); }
        }

        /// <summary>
        /// 通讯协议显示名，须与插件 <c>GetProtocolName()</c> 一致，如「Modbus TCP」。
        /// </summary>
        public string Protocol {
            get { return _protocol; }
            set { if (_protocol == value) return; _protocol = value; Raise("Protocol"); }
        }

        /// <summary>设备 IP 地址。</summary>
        public string Ip {
            get { return _ip; }
            set { if (_ip == value) return; _ip = value; Raise("Ip"); }
        }

        /// <summary>TCP 端口（Modbus 默认 502）。</summary>
        public int Port {
            get { return _port; }
            set { if (_port == value) return; _port = value; Raise("Port"); }
        }

        /// <summary>站号 / Unit Id。</summary>
        public int UnitId {
            get { return _unitId; }
            set { if (_unitId == value) return; _unitId = value; Raise("UnitId"); }
        }

        /// <summary>轨道类型（单轨 / 双轨）。</summary>
        public LaneType Lane {
            get { return _lane; }
            set {
                if (_lane == value) return;
                _lane = value;
                Raise("Lane");
                Raise("IsDualLane");
            }
        }

        /// <summary>
        /// 运行状态枚举。变更时同时通知 <see cref="StatusText"/>，供界面绑定状态文案。
        /// </summary>
        public DeviceStatusType StatusType {
            get { return _statusType; }
            set {
                if (_statusType == value) return;
                _statusType = value;
                Raise("StatusType");
                Raise("StatusText");
            }
        }

        /// <summary>是否已建立协议会话（与 StatusType 配合使用）。</summary>
        public bool IsConnected {
            get { return _isConnected; }
            set {
                if (_isConnected == value) return;
                _isConnected = value;
                Raise("IsConnected");
            }
        }

        /// <summary>
        /// 状态显示文案（只读，由 <see cref="StatusType"/> 推导）。
        /// </summary>
        public string StatusText {
            get {
                switch (StatusType) {
                    case DeviceStatusType.Success: return "RUN";
                    case DeviceStatusType.Connecting: return "连接中...";
                    case DeviceStatusType.Warning: return "警告";
                    case DeviceStatusType.Error: return "ALARM";
                    default: return "离线";
                }
            }
        }

        /// <summary>是否双轨（与 <see cref="Lane"/> 同步，便于绑定 CheckBox 等）。</summary>
        public bool IsDualLane {
            get { return Lane == LaneType.Dual; }
            set { Lane = value ? LaneType.Dual : LaneType.Single; }
        }

        /// <summary>默认值：离线、Modbus TCP、502 端口等。</summary>
        public DeviceInfo () {
            _id = System.Guid.NewGuid().ToString("N");
            _name = "新设备";
            _model = "";
            _protocol = "Modbus TCP";
            _ip = "192.168.0.1";
            _port = 502;
            _unitId = 1;
            _lane = LaneType.Single;
            _statusType = DeviceStatusType.Offline;
            _isConnected = false;
        }

        /// <summary>触发属性变更通知；值未变时各 setter 内已拦截，不会多余触发。</summary>
        protected void Raise (string name) {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(name));
        }
    }
}