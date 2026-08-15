using System.Windows.Controls;

namespace CommunicationDebuggingTools.Views.Pages.Settings {
    /// <summary>
    /// 系统设置页（一期占位）。
    /// 无参构造，由 DI Transient 注册；侧栏导航通过 GetRequiredService 创建。
    /// </summary>
    public partial class SettingsPage : Page {
        public SettingsPage () {
            InitializeComponent();
        }
    }
}