using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// “添加新 PLC”占位卡片。本身不携带任何设备数据，仅在被点击时找到所属的 <see cref="DevicePage"/>
    /// 并调用其 OpenAddDevice() 弹出新增设备的编辑面板。
    /// </summary>
    public partial class AddDeviceCard : UserControl {
        /// <summary>构造卡片并将鼠标样式设为手形，提示用户可点击。</summary>
        public AddDeviceCard () {
            InitializeComponent();
            Cursor = Cursors.Hand;
        }

        /// <summary>
        /// 卡片点击处理：优先从逻辑树向上查找父级 DevicePage；若未找到（例如控件被放入了
        /// 非直接父子关系的容器中），则回退到从所在窗口的可视树中搜索。找到后标记事件已处理，
        /// 避免上级容器（如 Window）重复处理同一次鼠标抬起事件。
        /// </summary>
        private void AddDeviceCard_MouseLeftButtonUp (object sender, MouseButtonEventArgs e) {
            if (e.Handled)
                return;

            DevicePage page = FindParentPage(this);
            if (page == null) {
                Window owner = Window.GetWindow(this);
                if (owner != null)
                    page = FindVisualDescendant<DevicePage>(owner);
            }

            if (page == null)
                return;

            e.Handled = true;
            page.OpenAddDevice();
        }

        /// <summary>
        /// 沿逻辑树（优先）/视觉树向上查找类型为 DevicePage 的祖先元素。
        /// FrameworkElement.Parent 作为 VisualTreeHelper.GetParent 的回退途径，适用于部分元素（如 Popup）不处于同一可视树的情况。
        /// </summary>
        private static DevicePage FindParentPage (DependencyObject d) {
            while (d != null) {
                DevicePage page = d as DevicePage;
                if (page != null)
                    return page;

                DependencyObject parent = VisualTreeHelper.GetParent(d);
                if (parent == null) {
                    FrameworkElement fe = d as FrameworkElement;
                    if (fe != null)
                        parent = fe.Parent as DependencyObject;
                }
                d = parent;
            }
            return null;
        }

        /// <summary>深度优先遍历视觉树，查找第一个类型为 T 的后代元素。</summary>
        private static T FindVisualDescendant<T> (DependencyObject root) where T : DependencyObject {
            if (root == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++) {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T result = child as T;
                if (result != null)
                    return result;

                result = FindVisualDescendant<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}