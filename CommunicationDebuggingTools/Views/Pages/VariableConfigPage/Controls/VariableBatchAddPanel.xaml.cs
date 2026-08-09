using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>批量添加变量弹层（对齐 var_batch_add_dialog）。</summary>
    public partial class VariableBatchAddPanel : UserControl {
        public event Action CloseRequested;
        public event Action<IList<VariableItem>> BatchSaveRequested;

        private readonly ObservableCollection<BatchRow> _rows = new ObservableCollection<BatchRow>();

        public VariableBatchAddPanel () {
            InitializeComponent();
            listRows.ItemsSource = _rows;
            _rows.CollectionChanged += (s, e) => RefreshCount();
        }

        /// <summary>打开前初始化：设备副标题 + 默认 3 行。</summary>
        public void Prepare (string deviceTitle) {
            txtSub.Text = deviceTitle ?? "";
            _rows.Clear();
            AddRows(3);
        }

        private void AddRows (int n) {
            for (int i = 0; i < n; i++)
                _rows.Add(BatchRow.Create());
            Renumber();
        }

        private void Renumber () {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Index = i + 1;
            RefreshCount();
        }

        private void RefreshCount () =>
            txtCount.Text = "当前 " + _rows.Count + " 行";

        private void BtnAddOne_Click (object sender, RoutedEventArgs e) {
            AddRows(1);
        }

        private void BtnAddFive_Click (object sender, RoutedEventArgs e) {
            AddRows(5);
        }

        private void BtnRemoveRow_Click (object sender, RoutedEventArgs e) {
            var row = (sender as FrameworkElement)?.Tag as BatchRow;
            if (row == null) return;
            _rows.Remove(row);
            Renumber();
        }

        private void BtnClose_Click (object sender, RoutedEventArgs e) =>
            CloseRequested?.Invoke();

        private void BtnSave_Click (object sender, RoutedEventArgs e) {
            var list = new List<VariableItem>();
            foreach (BatchRow r in _rows) {
                string name = (r.Name ?? "").Trim();
                string addr = (r.Address ?? "").Trim();
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(addr))
                    continue;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(addr)) {
                    MessageBox.Show("第 " + r.Index + " 行：名称和地址需同时填写（或整行留空跳过）",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                list.Add(r.ToItem());
            }

            if (list.Count == 0) {
                MessageBox.Show("没有可添加的行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BatchSaveRequested?.Invoke(list);
        }

        /// <summary>批量行编辑模型。</summary>
        public sealed class BatchRow : INotifyPropertyChanged {
            public event PropertyChangedEventHandler PropertyChanged;

            private int _index;
            private string _name = "";
            private string _address = "";
            private VariableDataType _dataType = VariableDataType.Int16;
            private string _accessText = "R/W";
            private string _unit = "";
            private string _category = "状态点";
            private string _description = "";

            public int Index {
                get => _index;
                set { _index = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Index))); }
            }

            public string Name {
                get => _name;
                set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
            }

            public string Address {
                get => _address;
                set { _address = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Address))); }
            }

            public VariableDataType DataType {
                get => _dataType;
                set { _dataType = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataType))); }
            }

            public string AccessText {
                get => _accessText;
                set { _accessText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessText))); }
            }

            public string Unit {
                get => _unit;
                set { _unit = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Unit))); }
            }

            public string Category {
                get => _category;
                set { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category))); }
            }

            public string Description {
                get => _description;
                set { _description = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); }
            }

            public IList<VariableDataType> DataTypeOptions { get; } =
                Enum.GetValues(typeof(VariableDataType)).Cast<VariableDataType>().ToList();

            public IList<string> AccessOptions { get; } =
                new[] { "R", "W", "R/W" };

            public IList<string> CategoryOptions { get; } =
                new[] { "状态点", "监控数据", "轨道宽度" };

            public static BatchRow Create () => new BatchRow();

            public VariableItem ToItem () {
                VariableAccess access = VariableAccess.ReadWrite;
                if (AccessText == "R") access = VariableAccess.ReadOnly;
                else if (AccessText == "W") access = VariableAccess.WriteOnly;

                return new VariableItem {
                    Name = (Name ?? "").Trim(),
                    Address = (Address ?? "").Trim(),
                    DataType = DataType,
                    Access = access,
                    Unit = (Unit ?? "").Trim(),
                    Category = Category ?? "状态点",
                    Description = (Description ?? "").Trim()
                };
            }
        }
    }
}