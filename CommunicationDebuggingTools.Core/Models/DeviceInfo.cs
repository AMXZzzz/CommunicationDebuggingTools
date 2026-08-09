using System.ComponentModel;
using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 设备信息模型（UI / 业务 / 持久化共用）。
    /// 协议私有连接参数仅存放在 <see cref="ProtocolSettingsJson"/>，由对应插件解析；
    /// 默认字节序、字序、字符串编码为设备级，变量一期直接继承。
    /// </summary>
    public class DeviceInfo : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _id;
        private string _name;
        private string _model;
        private string _protocol;
        private string _ip;
        private int _port;
        private LaneType _lane;
        private DeviceStatusType _statusType;
        private bool _isConnected;
        private ByteOrder _byteOrder;
        private WordOrder _wordOrder;
        private StringEncodingKind _stringEncoding;
        private string _protocolSettingsJson;

        /// <summary>唯一标识，新建时生成，持久化后不变。</summary>
        public string Id {
            get { return _id; }
            set { if (_id == value) return; _id = value; Raise("Id"); }
        }

        /// <summary>显示名称。</summary>
        public string Name {
            get { return _name; }
            set { if (_name == value) return; _name = value; Raise("Name"); }
        }

        /// <summary>品牌 / 型号。</summary>
        public string Model {
            get { return _model; }
            set { if (_model == value) return; _model = value; Raise("Model"); }
        }

        /// <summary>协议显示名，须与插件 GetProtocolName 一致。</summary>
        public string Protocol {
            get { return _protocol; }
            set { if (_protocol == value) return; _protocol = value; Raise("Protocol"); }
        }

        /// <summary>设备 IP。</summary>
        public string Ip {
            get { return _ip; }
            set { if (_ip == value) return; _ip = value; Raise("Ip"); }
        }

        /// <summary>通信端口。</summary>
        public int Port {
            get { return _port; }
            set { if (_port == value) return; _port = value; Raise("Port"); }
        }

        /// <summary>轨道类型。</summary>
        public LaneType Lane {
            get { return _lane; }
            set {
                if (_lane == value) return;
                _lane = value;
                Raise("Lane");
                Raise("IsDualLane");
            }
        }

        /// <summary>运行状态；变更时同步通知 StatusText。</summary>
        public DeviceStatusType StatusType {
            get { return _statusType; }
            set {
                if (_statusType == value) return;
                _statusType = value;
                Raise("StatusType");
                Raise("StatusText");
            }
        }

        /// <summary>是否已建立协议会话。</summary>
        public bool IsConnected {
            get { return _isConnected; }
            set { if (_isConnected == value) return; _isConnected = value; Raise("IsConnected"); }
        }

        /// <summary>设备默认字节序（一期变量读写继承此值）。</summary>
        public ByteOrder ByteOrder {
            get { return _byteOrder; }
            set { if (_byteOrder == value) return; _byteOrder = value; Raise("ByteOrder"); }
        }

        /// <summary>设备默认字序。</summary>
        public WordOrder WordOrder {
            get { return _wordOrder; }
            set { if (_wordOrder == value) return; _wordOrder = value; Raise("WordOrder"); }
        }

        /// <summary>设备默认字符串编码。</summary>
        public StringEncodingKind StringEncoding {
            get { return _stringEncoding; }
            set { if (_stringEncoding == value) return; _stringEncoding = value; Raise("StringEncoding"); }
        }

        /// <summary>
        /// 协议私有连接参数 JSON 原文。
        /// Core 不解析；Modbus 示例：{"unitId":1}；S7 示例：{"rack":0,"slot":1}。
        /// </summary>
        public string ProtocolSettingsJson {
            get { return _protocolSettingsJson; }
            set {
                if (_protocolSettingsJson == value) return;
                _protocolSettingsJson = value ?? "{}";
                Raise("ProtocolSettingsJson");
            }
        }

        /// <summary>状态文案（由 StatusType 推导）。</summary>
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

        /// <summary>是否双轨（与 Lane 同步）。</summary>
        public bool IsDualLane {
            get { return Lane == LaneType.Dual; }
            set { Lane = value ? LaneType.Dual : LaneType.Single; }
        }

        public DeviceInfo () {
            _id = System.Guid.NewGuid().ToString("N");
            _name = "新设备";
            _model = "";
            _protocol = "Modbus TCP";
            _ip = "192.168.0.1";
            _port = 502;
            _lane = LaneType.Single;
            _statusType = DeviceStatusType.Offline;
            _isConnected = false;
            _byteOrder = ByteOrder.BigEndian;
            _wordOrder = WordOrder.HighWordFirst;
            _stringEncoding = StringEncodingKind.Utf8;
            _protocolSettingsJson = "{\"unitId\":1}";
        }

        protected void Raise (string name) {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(name));
        }
    }
}