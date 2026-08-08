namespace CommunicationDebuggingTools.Views.Pages.Device {
    /// <summary>
    /// 设备列表末尾的「添加」占位（非真实设备）
    /// </summary>
    public sealed class AddDeviceMarker {
        public static readonly AddDeviceMarker Instance = new AddDeviceMarker();

        private AddDeviceMarker () {
        }
    }
}