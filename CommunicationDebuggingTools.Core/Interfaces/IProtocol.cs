using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {

    /// <summary>
    /// 通信协议插件的唯一对外契约。
    /// <para>
    /// 实现类位于 Plugin.* 程序集，由 <see cref="IProtocolResolver"/> 按协议显示名创建实例。
    /// 同一设备会话对应一个实例，禁止多设备共享同一 IProtocol 对象。
    /// </para>
    /// <para>
    /// 职责边界：
    /// - 本接口只描述「能做什么」（连接、探针、读、写）；
    /// - 「地址怎么解析、站号什么含义、报文怎么组」全部在实现类内部，不得泄漏到 Core/Business/UI。
    /// </para>
    /// </summary>
    public interface IProtocol {

        /// <summary>
        /// 协议显示名称，必须与设备 <c>DeviceInfo.Protocol</c>、
        /// 解析器注册名完全一致（例如 "Modbus TCP"、"Panasonic MEWTOCOL"）。
        /// UI 下拉框展示的也是该字符串。
        /// </summary>
        string GetProtocolName ();

        /// <summary>
        /// 当前是否已建立有效底层会话（如 TCP 已连接且流可用）。
        /// 不代表业务变量可读；仅表示传输层/会话层就绪。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 使用共性上下文建立与设备的会话。
        /// <para>
        /// 实现方应从 <paramref name="context"/> 读取 Ip、Port、StationNo、TimeoutMs 等；
        /// 需要扩展参数时只读取 <see cref="ProtocolConnectionContext.ExtraSettingsJson"/>，
        /// 且解析逻辑留在插件内。
        /// </para>
        /// <para>
        /// 成功返回 true；失败返回 false（并应释放已部分创建的资源）。
        /// 取消令牌触发时应尽快中止并清理，返回 false 或抛出 OperationCanceledException（由 Business 约定处理）。
        /// </para>
        /// </summary>
        /// <param name="context">Business 填充的共性连接参数，不得为 null。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否连接成功。</returns>
        Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// 断开会话并释放套接字等资源。应可重复调用（幂等），不抛出到 Business 层。
        /// </summary>
        void Disconnect ();

        /// <summary>
        /// 轻量探针：验证会话是否仍可用于通信（例如读一个无副作用地址）。
        /// <para>
        /// 返回 true 表示通讯正常；false 表示失败。
        /// 实现上应避免因探针失败而主动 Disconnect（是否断线由 Business 根据 IsConnected 与连续失败次数决定）。
        /// </para>
        /// </summary>
        Task<bool> PingAsync (CancellationToken cancellationToken);

        /// <summary>
        /// 按报文中的地址与数据类型读取设备数据。
        /// <para>
        /// 入参 <paramref name="request"/> 由 Business 填充 Address、DataType、Length、字节序等；
        /// 插件解析 Address，将结果写入 Value，并设置 Success / Quality / ErrorMessage。
        /// Address 对 Core 为不透明字符串（如 "40001"、"R10A"、"DB1.DBD0"）。
        /// </para>
        /// </summary>
        Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 按报文将 Value 写入设备。
        /// 成功时 Success=true；失败时 Success=false 并填写 ErrorMessage。
        /// 只写权限、类型不匹配等业务错误也应通过报文回填，而不是随意抛未处理异常
        /// （严重传输错误可抛，由 Business 捕获并记日志）。
        /// </summary>
        Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);
    }
}