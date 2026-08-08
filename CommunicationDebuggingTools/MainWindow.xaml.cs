using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CommunicationDebuggingTools {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window {
        private const string SizeExpandedKey = "SF.Size.NavBarExpanded";
        private const string SizeCollapsedKey = "SF.Size.NavBarCollapsed";



        private bool _isSidebarCollapsed;

        /// <summary>
        /// 构造函数
        /// </summary>
        public MainWindow () {
            //! 初始化设计器的布局
            InitializeComponent();

            //! 初始化首次呈现窗口
            MainFrame.Navigate(new Views.Pages.Monitor.DataMonitorPage());
        }

        /// <summary>
        /// 拖动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
            //! 按下时,触发App移动事件
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        /// <summary>
        /// 语言切换按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnLanguage_Click (object sender, RoutedEventArgs e) { }


        /// <summary>
        /// 全屏按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
        /// 最小化按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnMinimize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;

        }

        /// <summary>
        /// 最大化按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnMaximize_Click (object sender, RoutedEventArgs e) {
            WindowState = WindowState == WindowState.Maximized
               ? WindowState.Normal
               : WindowState.Maximized;
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnClose_Click (object sender, RoutedEventArgs e) {
            Close();        //! 关闭整个应用程序
        }


        /// <summary>
        /// 侧边栏折叠
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
        /// 导航切换
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
