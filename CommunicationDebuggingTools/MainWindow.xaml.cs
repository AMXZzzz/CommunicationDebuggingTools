using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

/*
 日志: 
    1. 没做实时读取
    3. 离线等状态为实时更新状态, 
    4. 考虑增加弹出的窗口可移动(上下边界可拖动)
    5. MES启动,自动开始连接
    6. 完美全屏优化(上方做成单独用户控件, 尝试完全自己写(增强知识))
 */




namespace CommunicationDebuggingTools {
    /// <summary>
    /// 主窗口：自定义无边框窗口，顶栏 + 侧栏（<see cref="Views.Controls.NavSidebar"/>）+ 内容 Frame。
    /// 导航由 NavSidebar 通过 NavigateRequested 通知，本窗口负责创建页面并 Navigate。
    /// </summary>
    public partial class MainWindow : Window {
        /// <summary>
        /// 初始化布局，订阅侧栏导航事件，并进入默认页（MES 监控）。
        /// </summary>
        public MainWindow () {
            InitializeComponent();

            if (navSidebar != null)
                navSidebar.NavigateRequested += NavSidebar_NavigateRequested;

            MainFrame.Navigate(new Views.Pages.Monitor.DataMonitorPage());
        }

        /// <summary>
        /// 侧栏选中导航项：按页面 Type 创建实例并显示到 MainFrame。
        /// </summary>
        private void NavSidebar_NavigateRequested (Type pageType) {
            if (MainFrame == null || pageType == null)
                return;

            if (!typeof(Page).IsAssignableFrom(pageType))
                return;

            Page page = Activator.CreateInstance(pageType) as Page;
            if (page != null)
                MainFrame.Navigate(page);
        }

        /// <summary>
        /// 顶栏拖动区域：按下左键拖动窗口。
        /// </summary>
        private void Window_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        /// <summary>
        /// 语言切换（预留）。
        /// </summary>
        private void BtnLanguage_Click (object sender, RoutedEventArgs e) {
        }

        /// <summary>
        /// 全屏 / 退出全屏。
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
        /// 最小化。
        /// </summary>
        private void BtnMinimize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化 / 还原。
        /// </summary>
        private void BtnMaximize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        /// 关闭应用程序。
        /// </summary>
        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            Close();
        }
    }
}