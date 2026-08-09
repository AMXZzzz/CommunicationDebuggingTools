using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>
    /// 当前设备下的变量表。
    /// 对齐 variable_config 模板：筛选 Tab、类型/读写/分类色块、
    /// 可写行的输入+写入、底栏统计、行悬停显示用途说明。
    /// </summary>
    public partial class VariableTable : UserControl {
        /// <summary>变量增删后通知页面刷新左侧数量等。</summary>
        public event Action VariablesChanged;

        /// <summary>请求打开编辑弹层。</summary>
        public event Action<VariableItem> EditRequested;

        /// <summary>请求写入 PLC（参数为变量 Id；二期接协议）。</summary>
        public event Action<string> WriteRequested;

        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private string _deviceId;
        private string _filter = "All";

        public VariableTable () {
            InitializeComponent();
            listRows.ItemsSource = _rows;
        }

        /// <summary>加载指定设备的变量列表。</summary>
        public void Load (string deviceId) {
            _deviceId = deviceId;
            Rebuild();
        }

        private void Filter_Checked (object sender, RoutedEventArgs e) {
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;
            _filter = tag;
            Rebuild();
        }

        private void BtnHint_Click (object sender, RoutedEventArgs e) {
            bool open = hintBar.Visibility != Visibility.Visible;
            hintBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            btnHint.Content = open ? "v" : "^";
        }

        /// <summary>按当前筛选重建行，并刷新 Tab/底栏计数。</summary>
        private void Rebuild () {
            _rows.Clear();
            int nAll = 0, nR = 0, nW = 0;

            if (string.IsNullOrEmpty(_deviceId) || MyAppServices.Variables == null) {
                txtEmpty.Visibility = Visibility.Visible;
                SetFooter(0, 0, 0);
                if (tabAll != null) tabAll.Content = "全部 (0)";
                return;
            }

            int index = 1;
            foreach (VariableItem v in MyAppServices.Variables.Variables) {
                if (v == null || v.DeviceId != _deviceId) continue;
                nAll++;
                if (v.Access == VariableAccess.ReadOnly) nR++;
                else nW++;

                if (!PassFilter(v)) continue;
                _rows.Add(Row.From(v, index++, this));
            }

            if (tabAll != null) tabAll.Content = "全部 (" + nAll + ")";
            SetFooter(nAll, nR, nW);
            txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetFooter (int all, int r, int w) {
            if (txtFtAll != null) txtFtAll.Text = "共 " + all + " 个变量";
            if (txtFtRead != null) txtFtRead.Text = "只读 " + r;
            if (txtFtWrite != null) txtFtWrite.Text = "可写 " + w;
        }

        private bool PassFilter (VariableItem v) {
            switch (_filter) {
                case "Read": return v.Access == VariableAccess.ReadOnly;
                case "Write": return v.Access != VariableAccess.ReadOnly;
                case "状态点": return v.Category == "状态点";
                case "监控数据": return v.Category == "监控数据";
                default: return true;
            }
        }

        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            string id = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(id) || MyAppServices.Variables == null) return;
            MyAppServices.Variables.Remove(id);
            Rebuild();
            VariablesChanged?.Invoke();
        }

        private void BtnEdit_Click (object sender, RoutedEventArgs e) {
            string id = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(id) || MyAppServices.Variables == null) return;
            foreach (VariableItem v in MyAppServices.Variables.Variables) {
                if (v != null && v.Id == id) {
                    EditRequested?.Invoke(v);
                    return;
                }
            }
        }

        private void BtnWrite_Click (object sender, RoutedEventArgs e) {
            string id = (sender as Button)?.Tag as string;
            if (!string.IsNullOrEmpty(id))
                WriteRequested?.Invoke(id);
        }

        private Brush B (string key) =>
            TryFindResource(key) as Brush ?? Brushes.Gray;

        /// <summary>表格行视图模型（仅 UI，不直接绑业务实体）。</summary>
        private sealed class Row {
            public string Id { get; set; }
            public int Index { get; set; }
            public string Name { get; set; }
            public string Address { get; set; }
            public string DataType { get; set; }
            public string AccessText { get; set; }
            public string ValueText { get; set; }
            public string WriteText { get; set; }
            public string UnitText { get; set; }
            public string Category { get; set; }
            /// <summary>用途说明（行悬停 ToolTip）。</summary>
            public string Description { get; set; }
            public Visibility DescToolTipVisibility { get; set; }

            public Brush TypeBg { get; set; }
            public Brush TypeFg { get; set; }
            public Brush AccessBg { get; set; }
            public Brush AccessFg { get; set; }
            public Brush CategoryBg { get; set; }
            public Brush CategoryFg { get; set; }
            public Brush ValueFg { get; set; }
            public Visibility ValueTextVisibility { get; set; }
            public Visibility WriteEditorVisibility { get; set; }

            public static Row From (VariableItem v, int index, VariableTable host) {
                bool canWrite = v.Access != VariableAccess.ReadOnly;
                string access = "R/W";
                if (v.Access == VariableAccess.ReadOnly) access = "R";
                else if (v.Access == VariableAccess.WriteOnly) access = "W";

                string cat = string.IsNullOrWhiteSpace(v.Category) ? "状态点" : v.Category;
                string val = v.LastValue != null ? v.LastValue.ToString() : "—";
                string desc = (v.Description ?? "").Trim();

                var row = new Row
                {
                    Id = v.Id,
                    Index = index,
                    Name = v.Name,
                    Address = v.Address,
                    DataType = v.DataType.ToString(),
                    AccessText = access,
                    ValueText = val,
                    WriteText = v.LastValue != null ? v.LastValue.ToString() : "",
                    UnitText = string.IsNullOrWhiteSpace(v.Unit) ? "—" : v.Unit,
                    Category = cat,
                    Description = desc,
                    // 无说明时不弹出空气泡
                    DescToolTipVisibility = string.IsNullOrEmpty(desc)
                        ? Visibility.Collapsed
                        : Visibility.Visible,
                    ValueTextVisibility = canWrite ? Visibility.Collapsed : Visibility.Visible,
                    WriteEditorVisibility = canWrite ? Visibility.Visible : Visibility.Collapsed,
                    ValueFg = host.B("SF.Brush.Text.Primary")
                };

                if (v.DataType == VariableDataType.Bool &&
                    string.Equals(val, "True", StringComparison.OrdinalIgnoreCase))
                    row.ValueFg = host.B("SF.Brush.Var.BoolOn");

                switch (v.DataType) {
                    case VariableDataType.Bool:
                        row.TypeBg = host.B("SF.Brush.Var.TypeBoolBg");
                        row.TypeFg = host.B("SF.Brush.Var.TypeBoolFg");
                        break;
                    case VariableDataType.Float:
                    case VariableDataType.Double:
                        row.TypeBg = host.B("SF.Brush.Var.TypeFloatBg");
                        row.TypeFg = host.B("SF.Brush.Var.TypeFloatFg");
                        break;
                    case VariableDataType.String:
                        row.TypeBg = host.B("SF.Brush.Var.TypeStringBg");
                        row.TypeFg = host.B("SF.Brush.Var.TypeStringFg");
                        break;
                    default:
                        row.TypeBg = host.B("SF.Brush.Var.TypeBg");
                        row.TypeFg = host.B("SF.Brush.Var.TypeFg");
                        break;
                }

                if (access == "R") {
                    row.AccessBg = host.B("SF.Brush.Var.RwRBg");
                    row.AccessFg = host.B("SF.Brush.Var.RwRFg");
                } else if (access == "W") {
                    row.AccessBg = host.B("SF.Brush.Var.RwWBg");
                    row.AccessFg = host.B("SF.Brush.Var.RwWFg");
                } else {
                    row.AccessBg = host.B("SF.Brush.Var.RwRwBg");
                    row.AccessFg = host.B("SF.Brush.Var.RwRwFg");
                }

                if (cat == "轨道宽度") {
                    row.CategoryBg = host.B("SF.Brush.Var.CatWidthBg");
                    row.CategoryFg = host.B("SF.Brush.Var.CatWidthFg");
                } else if (cat == "监控数据") {
                    row.CategoryBg = host.B("SF.Brush.Var.CatDataBg");
                    row.CategoryFg = host.B("SF.Brush.Var.CatDataFg");
                } else {
                    row.CategoryBg = host.B("SF.Brush.Var.CatStatusBg");
                    row.CategoryFg = host.B("SF.Brush.Var.CatStatusFg");
                }

                return row;
            }
        }
    }
}