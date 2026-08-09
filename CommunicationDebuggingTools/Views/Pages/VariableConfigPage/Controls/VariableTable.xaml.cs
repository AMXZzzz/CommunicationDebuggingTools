using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>
    /// 当前选中设备下的变量表。
    /// 删除在内部完成；编辑通过 <see cref="EditRequested"/> 交给页面打开弹层。
    /// </summary>
    public partial class VariableTable : UserControl {
        /// <summary>变量增删后通知页面刷新左侧设备「变量数量」。</summary>
        public event Action VariablesChanged;

        /// <summary>请求编辑指定变量（由页面打开 VariableEditPanel）。</summary>
        public event Action<VariableItem> EditRequested;

        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();

        /// <summary>当前展示的设备 Id。</summary>
        private string _deviceId;

        public VariableTable () {
            InitializeComponent();
            listRows.ItemsSource = _rows;
        }

        /// <summary>
        /// 加载指定设备下的变量行；deviceId 为空时显示空态。
        /// </summary>
        public void Load (string deviceId) {
            _deviceId = deviceId;
            _rows.Clear();

            if (string.IsNullOrEmpty(deviceId) || MyAppServices.Variables == null) {
                txtEmpty.Visibility = Visibility.Visible;
                return;
            }

            int index = 1;
            foreach (VariableItem v in MyAppServices.Variables.Variables) {
                if (v == null || v.DeviceId != deviceId)
                    continue;
                _rows.Add(Row.From(v, index++));
            }

            txtEmpty.Visibility = _rows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>编辑：按行 Id 找回 VariableItem，通知页面。</summary>
        private void BtnEdit_Click (object sender, RoutedEventArgs e) {
            string id = GetRowId(sender);
            if (string.IsNullOrEmpty(id) || MyAppServices.Variables == null)
                return;

            VariableItem item = FindById(id);
            if (item != null && EditRequested != null)
                EditRequested(item);
        }

        /// <summary>删除：写服务 → 刷新本表 → 通知左侧计数。</summary>
        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            string id = GetRowId(sender);
            if (string.IsNullOrEmpty(id) || MyAppServices.Variables == null)
                return;

            MyAppServices.Variables.Remove(id);
            Load(_deviceId);

            if (VariablesChanged != null)
                VariablesChanged();
        }

        private static string GetRowId (object sender) {
            Button btn = sender as Button;
            return btn != null ? btn.Tag as string : null;
        }

        private static VariableItem FindById (string id) {
            foreach (VariableItem v in MyAppServices.Variables.Variables) {
                if (v != null && v.Id == id)
                    return v;
            }
            return null;
        }

        /// <summary>列表行展示模型（与 VariableItem 解耦，只含 UI 需要的字段）。</summary>
        private sealed class Row {
            public string Id { get; set; }
            public int Index { get; set; }
            public string Name { get; set; }
            public string Address { get; set; }
            public string DataType { get; set; }
            public string AccessText { get; set; }
            public string ValueText { get; set; }

            public static Row From (VariableItem v, int index) {
                string access = "R/W";
                if (v.Access == VariableAccess.ReadOnly)
                    access = "R";
                else if (v.Access == VariableAccess.WriteOnly)
                    access = "W";

                return new Row {
                    Id = v.Id,
                    Index = index,
                    Name = v.Name,
                    Address = v.Address,
                    DataType = v.DataType.ToString(),
                    AccessText = access,
                    ValueText = v.LastValue != null ? v.LastValue.ToString() : "—"
                };
            }
        }
    }
}