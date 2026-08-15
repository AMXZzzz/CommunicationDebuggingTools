using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>
    /// 变量表：筛选与计数；徽章颜色由 XAML DataTrigger 负责。
    /// </summary>
    public partial class VariableTable : UserControl {
        /// <summary>
        /// 从 DI 容器解析服务。
        /// UserControl 由 XAML 实例化，无法构造注入；此为迁移期过渡方案。
        /// 全局只有 Singleton，GetRequiredService 成本极低。
        /// </summary>
        private static T Svc<T> () where T : class =>
            CommunicationDebuggingTools.App.Services
                .GetRequiredService<T>();

        public event Action VariablesChanged;
        public event Action<VariableItem> EditRequested;
        public event Action<string, string> WriteRequested;

        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private string _deviceId;
        private string _filterTag = "All";

        public VariableTable () {
            InitializeComponent();
            listRows.ItemsSource = _rows;
        }

        public void Load (string deviceId) {
            _deviceId = deviceId;
            Rebuild();
        }

        private void Filter_Checked (object sender, RoutedEventArgs e) {
            var rb = sender as RadioButton;
            if (rb == null || rb.Tag == null) return;
            _filterTag = rb.Tag.ToString();
            Rebuild();
        }

        private void BtnHint_Click (object sender, RoutedEventArgs e) {
            if (hintBar == null) return;
            hintBar.Visibility = hintBar.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnEdit_Click (object sender, RoutedEventArgs e) {
            string id = (sender as FrameworkElement)?.Tag as string;
            VariableItem item = FindVariable(id);
            if (item != null) EditRequested?.Invoke(item);
        }

        private void BtnDelete_Click (object sender, RoutedEventArgs e) {
            string id = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(id) || Svc<IVariableService>() == null) return;
            Svc<IVariableService>().Remove(id);
            Rebuild();
            VariablesChanged?.Invoke();
        }

        private void BtnWrite_Click (object sender, RoutedEventArgs e) {
            string id = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(id)) return;
            foreach (Row r in _rows) {
                if (r.Id == id) {
                    WriteRequested?.Invoke(id, r.WriteText ?? "");
                    return;
                }
            }
        }

        private void Rebuild () {
            _rows.Clear();
            int all = 0, read = 0, write = 0, status = 0, data = 0;

            if (string.IsNullOrEmpty(_deviceId) || Svc<IVariableService>() == null) {
                UpdateTabCounts(0, 0, 0, 0, 0);
                UpdateFooter(0, 0, 0);
                txtEmpty.Visibility = Visibility.Visible;
                return;
            }

            int index = 1;
            foreach (VariableItem v in Svc<IVariableService>().Variables) {
                if (v == null || v.DeviceId != _deviceId) continue;

                all++;
                if (v.Access == VariableAccess.ReadOnly) read++;
                if (v.Access == VariableAccess.WriteOnly || v.Access == VariableAccess.ReadWrite)
                    write++;
                if (v.Category == "状态点") status++;
                if (v.Category == "监控数据") data++;

                if (!PassFilter(v)) continue;
                _rows.Add(Row.From(v, index++));
            }

            UpdateTabCounts(all, read, write, status, data);
            UpdateFooter(all, read, write);
            txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool PassFilter (VariableItem v) {
            if (string.IsNullOrEmpty(_filterTag) || _filterTag == "All") return true;
            switch (_filterTag) {
                case "Read": return v.Access == VariableAccess.ReadOnly;
                case "Write":
                    return v.Access == VariableAccess.WriteOnly
                        || v.Access == VariableAccess.ReadWrite;
                case "状态点": return v.Category == "状态点";
                case "监控数据": return v.Category == "监控数据";
                default: return true;
            }
        }

        private void UpdateTabCounts (int all, int read, int write, int status, int data) {
            if (tabAll != null) tabAll.Content = string.Format("全部 ({0})", all);
            if (tabRead != null) tabRead.Content = string.Format("只读 ({0})", read);
            if (tabWrite != null) tabWrite.Content = string.Format("可写 ({0})", write);
            if (tabStatus != null) tabStatus.Content = string.Format("状态点 ({0})", status);
            if (tabData != null) tabData.Content = string.Format("监控数据 ({0})", data);
        }

        private void UpdateFooter (int all, int read, int write) {
            if (txtFtAll != null) txtFtAll.Text = string.Format("共 {0} 个变量", all);
            if (txtFtRead != null) txtFtRead.Text = string.Format("只读 {0}", read);
            if (txtFtWrite != null) txtFtWrite.Text = string.Format("可写 {0}", write);
        }

        private static VariableItem FindVariable (string id) {
            if (string.IsNullOrEmpty(id) || Svc<IVariableService>() == null) return null;
            foreach (VariableItem v in Svc<IVariableService>().Variables)
                if (v != null && v.Id == id) return v;
            return null;
        }

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
            public string Description { get; set; }
            public Visibility ValueTextVisibility { get; set; }
            public Visibility WriteEditorVisibility { get; set; }
            public Visibility DescToolTipVisibility { get; set; }

            public static Row From (VariableItem v, int index) {
                string access = "R/W";
                if (v.Access == VariableAccess.ReadOnly) access = "R";
                else if (v.Access == VariableAccess.WriteOnly) access = "W";

                bool canWrite = v.Access == VariableAccess.WriteOnly
                             || v.Access == VariableAccess.ReadWrite;
                string desc = v.Description ?? "";

                return new Row {
                    Id = v.Id,
                    Index = index,
                    Name = v.Name ?? "",
                    Address = v.Address ?? "",
                    DataType = v.DataType.ToString(),
                    AccessText = access,
                    ValueText = v.LastValue != null ? v.LastValue.ToString() : "—",
                    WriteText = v.LastValue != null ? v.LastValue.ToString() : "",
                    UnitText = string.IsNullOrWhiteSpace(v.Unit) ? "—" : v.Unit,
                    Category = string.IsNullOrWhiteSpace(v.Category) ? "—" : v.Category,
                    Description = desc,
                    ValueTextVisibility = canWrite ? Visibility.Collapsed : Visibility.Visible,
                    WriteEditorVisibility = canWrite ? Visibility.Visible : Visibility.Collapsed,
                    DescToolTipVisibility = string.IsNullOrWhiteSpace(desc)
                        ? Visibility.Collapsed : Visibility.Visible
                };
            }
        }
    }
}