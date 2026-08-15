using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Logging;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 变量配置页 ViewModel（一期占位）。
    /// 页面逻辑仍在 <c>VariableConfigPage</c> code-behind + Svc&lt;T&gt;；
    /// 本类仅满足 DI 注册，后续再把读写/筛选迁入此处。
    /// </summary>
    public sealed class VariablePageViewModel : ViewModelBase {

        private readonly IVariableService _variables;
        private readonly IDeviceService _devices;
        private readonly IAppLogger _log;

        public VariablePageViewModel (
            IVariableService variables,
            IDeviceService devices,
            IAppLogger log) {
            _variables = variables;
            _devices = devices;
            _log = log;
        }
    }
}