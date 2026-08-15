using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;
using System;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 变量配置页 ViewModel（占位）。
    /// <para>
    /// 当前业务仍在 <c>VariableConfigPage</c> code-behind：
    /// 页面构造注入 <see cref="IVariableService"/> / <see cref="IDeviceService"/>，
    /// 再属性注入到子控件，已不再使用 Svc / App.Services。
    /// </para>
    /// <para>
    /// 本类已在 DI 中注册，供后续把读写、筛选、导入导出迁入 MVVM 时使用；
    /// 一期可不注入到页面。
    /// </para>
    /// </summary>
    public sealed class VariablePageViewModel : ViewModelBase {

        private readonly IVariableService _variables;
        private readonly IDeviceService _devices;
        private readonly IAppLogger _log;

        public VariablePageViewModel (
            IVariableService variables,
            IDeviceService devices,
            IAppLogger log) {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _log = log;
        }
    }
}