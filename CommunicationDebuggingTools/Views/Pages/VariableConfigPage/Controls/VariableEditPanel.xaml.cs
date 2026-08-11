using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>
    /// 变量添加/编辑弹层（对齐设备编辑弹窗与 var_add_dialog）。
    /// 通过 Close / Save / Delete 事件将意图交给页面处理。
    /// </summary>
    public partial class VariableEditPanel : UserControl {
        /// <summary>关闭弹层（不保存）。</summary>
        public event Action CloseRequested;

        /// <summary>保存当前表单。</summary>
        public event Action SaveRequested;

        /// <summary>删除当前编辑中的变量（仅编辑模式）。</summary>
        public event Action DeleteRequested;

        private string _editingId;
        private VariableAccess _access = VariableAccess.ReadOnly;
        private string _category = "状态点";

        /// <summary>是否新增。</summary>
        public bool IsNew => string.IsNullOrEmpty(_editingId);

        /// <summary>编辑中的变量 Id；新增为 null。</summary>
        public string EditingId => _editingId;

        public VariableEditPanel () {
            InitializeComponent();

            cmbDataType.Items.Clear();
            foreach (VariableDataType t in Enum.GetValues(typeof(VariableDataType)))
                cmbDataType.Items.Add(t);
            cmbDataType.SelectedItem = VariableDataType.Int16;

            UpdateAccessButtons();
            UpdateCategoryButtons();
        }

        /// <summary>切换为新增并清空表单。</summary>
        public void PrepareNew () {
            _editingId = null;
            txtTitle.Text = "添加变量";
            btnSave.Content = "添加";
            if (btnDelete != null)
                btnDelete.Visibility = Visibility.Collapsed;

            txtName.Text = "";
            txtAddress.Text = "";
            txtUnit.Text = "";
            txtDesc.Text = "";
            cmbDataType.SelectedItem = VariableDataType.Int16;

            _access = VariableAccess.ReadOnly;
            _category = "状态点";
            UpdateAccessButtons();
            UpdateCategoryButtons();
        }

        /// <summary>切换为编辑并填充字段。</summary>
        public void Load (VariableItem v) {
            if (v == null) {
                PrepareNew();
                return;
            }

            _editingId = v.Id;
            txtTitle.Text = "编辑变量";
            btnSave.Content = "保存";
            if (btnDelete != null)
                btnDelete.Visibility = Visibility.Visible;

            txtName.Text = v.Name ?? "";
            txtAddress.Text = v.Address ?? "";
            txtUnit.Text = v.Unit ?? "";
            txtDesc.Text = v.Description ?? "";
            cmbDataType.SelectedItem = v.DataType;

            _access = v.Access;
            _category = string.IsNullOrEmpty(v.Category) ? "状态点" : v.Category;
            UpdateAccessButtons();
            UpdateCategoryButtons();
        }

        /// <summary>收集表单；校验失败返回 null。</summary>
        public VariableItem Build () {
            string name = (txtName.Text ?? "").Trim();
            string address = (txtAddress.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(address)) {
                MessageBox.Show("显示名称和地址不能为空", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var item = new VariableItem
            {
                Name = name,
                Address = address,
                Unit = (txtUnit.Text ?? "").Trim(),
                Description = (txtDesc.Text ?? "").Trim(),
                DataType = cmbDataType.SelectedItem is VariableDataType dt
                    ? dt
                    : VariableDataType.Int16,
                Access = _access,
                Category = _category
            };

            if (!string.IsNullOrEmpty(_editingId))
                item.Id = _editingId;

            return item;
        }

        private void BtnAccess_Click (object sender, RoutedEventArgs e) {
            string tag = (sender as FrameworkElement)?.Tag as string;
            if (tag == "WriteOnly")
                _access = VariableAccess.WriteOnly;
            else if (tag == "ReadWrite")
                _access = VariableAccess.ReadWrite;
            else
                _access = VariableAccess.ReadOnly;
            UpdateAccessButtons();
        }

        private void BtnCategory_Click (object sender, RoutedEventArgs e) {
            string tag = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(tag))
                _category = tag;
            UpdateCategoryButtons();
        }

        private void UpdateAccessButtons () {
            Style dark = TryFindResource("SF.Style.DarkButton") as Style;
            Style primary = TryFindResource("SF.Style.PrimaryButton") as Style;
            btnAccR.Style = _access == VariableAccess.ReadOnly ? primary : dark;
            btnAccW.Style = _access == VariableAccess.WriteOnly ? primary : dark;
            btnAccRW.Style = _access == VariableAccess.ReadWrite ? primary : dark;
        }

        private void UpdateCategoryButtons () {
            Style dark = TryFindResource("SF.Style.DarkButton") as Style;
            Style primary = TryFindResource("SF.Style.PrimaryButton") as Style;
            btnCatStatus.Style = _category == "状态点" ? primary : dark;
            btnCatData.Style = _category == "监控数据" ? primary : dark;
            btnCatWidth.Style = _category == "轨道宽度" ? primary : dark;
        }

        private void BtnClose_Click (object sender, RoutedEventArgs e) =>
            CloseRequested?.Invoke();

        private void Root_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) =>
            e.Handled = true;

        private void BtnSave_Click (object sender, RoutedEventArgs e) =>
            SaveRequested?.Invoke();

        private void BtnDelete_Click (object sender, RoutedEventArgs e) =>
            DeleteRequested?.Invoke();
    }
}