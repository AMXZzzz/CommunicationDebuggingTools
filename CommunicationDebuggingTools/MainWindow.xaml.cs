using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommunicationDebuggingTools {
    /// <summary>
    /// 主窗口：自定义窗口框体（无系统标题栏），包含顶部拖动区域、侧边导航栏与主内容 Frame。
    /// 导航栏通过 RadioButton.Tag 携带页面类型，点击后反射创建实例并导航到 MainFrame。
    /// </summary>
    public partial class MainWindow : Window {
        /// <summary>侧边导航栏展开状态下的宽度资源 Key。</summary>
        private const string SizeExpandedKey = "SF.Size.NavBarExpanded";
        /// <summary>侧边导航栏折叠状态下的宽度资源 Key。</summary>
        private const string SizeCollapsedKey = "SF.Size.NavBarCollapsed";

        /// <summary>侧边导航栏当前是否处于折叠状态。</summary>
        private bool _isSidebarCollapsed;

        /// <summary>
        /// 构造函数：初始化组件并导航到首页（数据监控页）。
        /// </summary>
        public MainWindow () {
            //! 初始化设计器的布局
            InitializeComponent();

            //! 初始化首次呈现窗口
            MainFrame.Navigate(new Views.Pages.Monitor.DataMonitorPage());
        }

        /// <summary>
        /// 顶部标题栏区域的鼠标按下事件：触发窗口拖动。
        /// 注意：为避免干扰内容区域内控件的点击事件，该处理只应绑定在顶部标题栏 Border 上，而非整个 Window。
        /// </summary>
        private void Window_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            //! 按下时,触发App移动事件
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        /// <summary>
        /// 语言切换按钮点击事件（预留扩展点，当前尚未实现多语言切换逻辑）。
        /// </summary>
        private void BtnLanguage_Click (object sender, RoutedEventArgs e) { }


        /// <summary>
        /// 全屏按钮点击事件：在最大化与普通状态之间切换，并同步更新按钮文本。
        /// </summary>
        private void BtnFullscreen_Click (object sender, RoutedEventArgs e) {
            if (WindowState != WindowState.Maximized) {
                WindowState = WindowState.Maximized;
                btnFullscreen.Content = "退出全屏";
            } else {
                WindowState = WindowState.Normal;
                btnFullscreen.Content = "全屏";
            }
        }

        /// <summary>
        /// 最小化按钮点击事件。
        /// </summary>
        private void BtnMinimize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;

        }

        /// <summary>
        /// 最大化按钮点击事件：在最大化与普通状态之间切换。
        /// </summary>
        private void BtnMaximize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState == WindowState.Maximized
               ? WindowState.Normal
               : WindowState.Maximized;
        }

        /// <summary>
        /// 关闭按钮点击事件：关闭整个应用程序。
        /// </summary>
        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            Close();        //! 关闭整个应用程序
        }


        /// <summary>
        /// 侧边栏折叠/展开按钮点击事件：以动画方式过渡侧边栏宽度，提升交互体验。
        /// </summary>
        private void BtnToggleSidebar_Click (object sender, RoutedEventArgs e) {
            _isSidebarCollapsed = !_isSidebarCollapsed;

            double from = SidebarBorder.Width;
            double to = _isSidebarCollapsed
                ? (double)FindResource(SizeExpandedKey)
                : (double)FindResource(SizeCollapsedKey);

            var animation = new System.Windows.Media.Animation.DoubleAnimation {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };

            SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }

        /// <summary>
        /// 导航切换：根据被选中 RadioButton 的 Tag（页面类型）创建对应页面实例并导航到 MainFrame。
        /// 该方式避免为每个导航项编写重复的 if/else 分支，新增页面只需在 XAML 中配置 Tag 即可。
        /// </summary>
        private void Nav_Checked (object sender, RoutedEventArgs e) {
            //! 判空
            if (MainFrame == null) return;

            //! 有效校验
            if (!(sender is RadioButton rb)) return;

            // Tag 直接就是页面类型
            if (rb.Tag is Type pageType && typeof(Page).IsAssignableFrom(pageType)) {
                if (Activator.CreateInstance(pageType) is Page page) {
                    MainFrame.Navigate(page);
                }
            }
        }
    }
}
