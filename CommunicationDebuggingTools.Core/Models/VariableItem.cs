using System.ComponentModel;
using CommunicationDebuggingTools.Core.Enums;

namespace CommunicationDebuggingTools.Core.Models {
    /// <summary>
    /// 变量（点表）配置项。
    /// Address 为不透明字符串，仅由对应协议插件解析；
    /// 一期字序/编码继承设备默认，读写时写入 ProtocolDataMessage。
    /// </summary>
    public class VariableItem : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _id;
        private string _deviceId;
        private string _name;
        private string _address;
        private VariableDataType _dataType;
        private VariableAccess _access;
        private int _length;
        private object _lastValue;
        private string _lastError;
        private DataQuality _quality;
        private string _unit;
        private string _category;
        private string _description;

        /// <summary>唯一标识。</summary>
        public string Id {
            get => _id;
            set { if (_id == value) return; _id = value; Raise(nameof(Id)); }
        }

        /// <summary>所属设备 Id（DeviceInfo.Id）。</summary>
        public string DeviceId {
            get => _deviceId;
            set { if (_deviceId == value) return; _deviceId = value; Raise(nameof(DeviceId)); }
        }

        /// <summary>显示名称。</summary>
        public string Name {
            get => _name;
            set { if (_name == value) return; _name = value; Raise(nameof(Name)); }
        }

        /// <summary>协议地址原文（如 40001、DB1.DBD0、R1A）。</summary>
        public string Address {
            get => _address;
            set { if (_address == value) return; _address = value; Raise(nameof(Address)); }
        }

        /// <summary>数据类型。</summary>
        public VariableDataType DataType {
            get => _dataType;
            set { if (_dataType == value) return; _dataType = value; Raise(nameof(DataType)); }
        }

        /// <summary>读写权限。</summary>
        public VariableAccess Access {
            get => _access;
            set { if (_access == value) return; _access = value; Raise(nameof(Access)); }
        }

        /// <summary>字符串最大长度等；数值型可为 0。</summary>
        public int Length {
            get => _length;
            set { if (_length == value) return; _length = value; Raise(nameof(Length)); }
        }

        /// <summary>工程单位（pcs / mm 等），可选。</summary>
        public string Unit {
            get => _unit;
            set { if (_unit == value) return; _unit = value; Raise(nameof(Unit)); }
        }

        /// <summary>分类：状态点 | 监控数据 | 轨道宽度。</summary>
        public string Category {
            get => _category;
            set { if (_category == value) return; _category = value; Raise(nameof(Category)); }
        }

        /// <summary>用途说明，可选。</summary>
        public string Description {
            get => _description;
            set { if (_description == value) return; _description = value; Raise(nameof(Description)); }
        }

        /// <summary>最近一次成功读回或写入的值（运行时，可不持久化）。</summary>
        public object LastValue {
            get => _lastValue;
            set { if (Equals(_lastValue, value)) return; _lastValue = value; Raise(nameof(LastValue)); }
        }

        /// <summary>最近一次失败信息。</summary>
        public string LastError {
            get => _lastError;
            set { if (_lastError == value) return; _lastError = value; Raise(nameof(LastError)); }
        }

        /// <summary>读回质量。</summary>
        public DataQuality Quality {
            get => _quality;
            set { if (_quality == value) return; _quality = value; Raise(nameof(Quality)); }
        }

        public VariableItem () {
            _id = System.Guid.NewGuid().ToString("N");
            _deviceId = "";
            _name = "新变量";
            _address = "";
            _dataType = VariableDataType.Int16;
            _access = VariableAccess.ReadWrite;
            _length = 0;
            _unit = "";
            _category = "状态点";
            _description = "";
            _lastError = "";
            _quality = DataQuality.Bad;
            _lastValue = null;
        }

        protected void Raise (string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}