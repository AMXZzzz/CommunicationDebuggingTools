using System;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Core.Models;

namespace CommunicationDebuggingTools.Core.Interfaces {

    /// <summary>
    /// 通信协议插件的唯一对外契约。继承 IDisposable 以保证资源能被统一释放。
    ///
    /// 生命周期约定：
    ///   ConnectAsync → [ReadAsync / WriteAsync / PingAsync 反复调用] → Disconnect / Dispose
    ///
    /// Disconnect 与 Dispose 的区别：
    ///   Disconnect：关闭当前会话，对象可再次 ConnectAsync（连接可复用场景）。
    ///   Dispose   ：最终销毁，释放全部托管/非托管资源，之后不得再调用任何方法。
    ///               实现必须在 Dispose 内调用 Disconnect（幂等，不重复断线）。
    ///
    /// 职责边界：
    ///   - 本接口只描述「能做什么」；
    ///   - 「地址解析、报文组装、站号含义」全在实现类内部，禁止向上泄漏。
    ///   - ReadAsync / WriteAsync 的 Address 对 Core 是不透明字符串。
    /// </summary>
    public interface IProtocol : IDisposable {

        /// <summary>
        /// 协议显示名称；须与 DeviceInfo.Protocol、ProtocolResolver 注册名完全一致。
        /// </summary>
        string GetProtocolName ();

        /// <summary>
        /// 传输层会话是否可用（TCP 已连接且流可读写）。
        /// 注意：true 不代表上层通信正常，PingAsync 才能验证通信层。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 建立与设备的会话。
        /// 成功返回 true；失败返回 false 并释放已创建的部分资源。
        /// 取消时返回 false 或抛 OperationCanceledException，由 Business 层统一处理。
        /// </summary>
        Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// 断开当前会话。幂等，可重复调用，不抛出到 Business 层。
        /// 断开后对象可再次 ConnectAsync。
        /// </summary>
        void Disconnect ();

        /// <summary>
        /// 轻量探针：先做 TCP 层检测（Socket.Poll），再发一次无副作用的协议读请求。
        /// 返回 true 表示通信正常；false 表示某一层失败。
        /// 实现不应在探针失败时主动 Disconnect（由 Business 根据 IsConnected 与失败计数决定）。
        /// </summary>
        Task<bool> PingAsync (CancellationToken cancellationToken);

        /// <summary>
        /// 读取一个变量。实现解析 request.Address，将结果写入 Value / Success / Quality / ErrorMessage。
        /// </summary>
        Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 写入一个变量。成功时 Success=true；失败时 Success=false + ErrorMessage。
        /// 严重传输错误可抛异常，由 Business 捕获记录。
        /// </summary>
        Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken);
    }
}