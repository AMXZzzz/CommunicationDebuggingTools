using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Settings {

    /// <summary>
    /// 系统设置页：本地模式 / 远端 EngineHost 切换。
    /// DataContext 绑定 SettingsViewModel（或直接在 code-behind 操作控件）。
    /// 重启生效——模式切换涉及 DI 容器重建，简单可靠。
    /// </summary>
    public partial class SettingsPage : Page {

        private readonly AppSettings _settings;
        private readonly EngineHostChannel _channel;

        public SettingsPage () {
            InitializeComponent();
            _settings = AppSettings.Load();
            _channel  = App.Services?.GetService(typeof(EngineHostChannel)) as EngineHostChannel;
            Loaded += OnLoaded;
        }

        private void OnLoaded (object sender, RoutedEventArgs e) {
            if (chkRemote  != null) chkRemote.IsChecked   = _settings.RemoteMode;
            if (txtAddress != null) txtAddress.Text        = _settings.HostAddress;
            RefreshControls();
        }

        // ── 切换本地 / 远端 ──────────────────────────

        private void ChkRemote_Changed (object sender, RoutedEventArgs e) => RefreshControls();

        private void RefreshControls () {
            bool remote = chkRemote?.IsChecked == true;
            if (pnlRemote   != null) pnlRemote.Visibility   = remote ? Visibility.Visible : Visibility.Collapsed;
            if (lblRestart  != null) lblRestart.Visibility  = Visibility.Collapsed;
        }

        // ── 测试连接 ──────────────────────────────────

        private async void BtnTest_Click (object sender, RoutedEventArgs e) {
            if (btnTest     != null) btnTest.IsEnabled     = false;
            if (lblTestResult != null) lblTestResult.Text  = "连接中...";

            string addr = txtAddress?.Text?.Trim() ?? AppSettings.DefaultHostAddress;
            try {
                _channel?.Open(addr);
                bool ok = await (_channel?.PingAsync(CancellationToken.None)
                          ?? System.Threading.Tasks.Task.FromResult(false))
                    .ConfigureAwait(true);

                if (lblTestResult != null)
                    lblTestResult.Text = ok ? "✔ 连接成功" : "✘ 无法到达 EngineHost";
            } catch (Exception ex) {
                if (lblTestResult != null) lblTestResult.Text = "✘ " + ex.Message;
            } finally {
                if (btnTest != null) btnTest.IsEnabled = true;
            }
        }

        // ── 保存 ──────────────────────────────────────

        private void BtnSave_Click (object sender, RoutedEventArgs e) {
            _settings.RemoteMode  = chkRemote?.IsChecked == true;
            _settings.HostAddress = txtAddress?.Text?.Trim() ?? AppSettings.DefaultHostAddress;
            _settings.Save();

            if (lblRestart != null) {
                lblRestart.Text       = "✔ 已保存，重启应用后生效";
                lblRestart.Visibility = Visibility.Visible;
            }
        }
    }
}
