using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.VariableConfigPage.Controls {
    /// <summary>右侧当前设备标题与操作按钮。</summary>
    public partial class VariableDeviceHeader : UserControl {
        /// <summary>请求添加单条变量。</summary>
        public event Action AddClicked;

        /// <summary>请求打开批量添加。</summary>
        public event Action BatchAddClicked;

        public VariableDeviceHeader () {
            InitializeComponent();
        }

        /// <summary>按设备 Id 刷新标题与副标题。</summary>
        public void Show (string deviceId) {
            if (string.IsNullOrEmpty(deviceId) || MyAppServices.Devices == null) {
                txtTitle.Text = "请选择设备";
                txtMeta.Text = "";
                return;
            }

            DeviceInfo d = MyAppServices.Devices.Devices.FirstOrDefault(x => x != null && x.Id == deviceId);
            if (d == null) {
                txtTitle.Text = "请选择设备";
                txtMeta.Text = "";
                return;
            }

            txtTitle.Text = string.IsNullOrEmpty(d.Name) ? d.Id : d.Name;
            txtMeta.Text = string.Format("{0} · {1} · {2}",
                d.Model ?? "", d.Ip ?? "", d.Protocol ?? "");
        }

        private void BtnAdd_Click (object sender, RoutedEventArgs e) =>
            AddClicked?.Invoke();

        private void BtnBatchAdd_Click (object sender, RoutedEventArgs e) =>
            BatchAddClicked?.Invoke();
    }
}