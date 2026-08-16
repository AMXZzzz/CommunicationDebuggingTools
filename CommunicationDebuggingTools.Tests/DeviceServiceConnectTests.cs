using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Plugins;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommunicationDebuggingTools.Tests {
    /// <summary>
    /// DeviceService 连接流程单测。
    /// 真实顺序：探测端口 → 解析插件 → ProtocolConnectionContext 建连。
    /// </summary>
    [TestClass]
    public class DeviceServiceConnectTests {
        /// <summary>协议连接成功（需 Ip:Port 探测可通过，例如本机有进程监听）。</summary>
        [TestMethod]
        public async Task ConnectAsync_WhenProtocolOk_ShouldBeSuccess () {
            var protocol = new FakeProtocol { ConnectResult = true };
            var resolver = new FakeProtocolResolver { ProtocolToReturn = protocol };
            var svc = new DeviceService(resolver, new FakeDeviceRepository());

            var device = CreateDevice("127.0.0.1", 502);
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            // 若探测失败，会是 Offline 且 ConnectCallCount==0
            if (!ok && device.StatusType == DeviceStatusType.Offline) {
                Assert.Inconclusive("TCP 探测失败（127.0.0.1:502 未监听），无法验证协议成功分支");
                return;
            }

            Assert.IsTrue(ok);
            Assert.IsTrue(device.IsConnected);
            Assert.AreEqual(DeviceStatusType.Success, device.StatusType);
            Assert.AreEqual(1, protocol.ConnectCallCount);
            Assert.IsNotNull(protocol.LastContext);
            Assert.AreEqual("127.0.0.1", protocol.LastContext.Ip);
            Assert.AreEqual(502, protocol.LastContext.Port);
        }

        /// <summary>插件返回连接失败 → Error。</summary>
        [TestMethod]
        public async Task ConnectAsync_WhenProtocolFails_ShouldBeError () {
            var protocol = new FakeProtocol { ConnectResult = false };
            var resolver = new FakeProtocolResolver { ProtocolToReturn = protocol };
            var svc = new DeviceService(resolver, new FakeDeviceRepository());

            var device = CreateDevice("127.0.0.1", 502);
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            if (device.StatusType == DeviceStatusType.Offline) {
                Assert.Inconclusive("TCP 探测失败，未进入协议连接分支");
                return;
            }

            Assert.IsFalse(ok);
            Assert.IsFalse(device.IsConnected);
            Assert.AreEqual(DeviceStatusType.Error, device.StatusType);
        }

        /// <summary>解析不到插件 → Error（同样依赖探测先成功）。</summary>
        [TestMethod]
        public async Task ConnectAsync_WhenProtocolMissing_ShouldBeError () {
            var resolver = new FakeProtocolResolver { ProtocolToReturn = null };
            var svc = new DeviceService(resolver, new FakeDeviceRepository());

            var device = CreateDevice("127.0.0.1", 502);
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            if (device.StatusType == DeviceStatusType.Offline) {
                Assert.Inconclusive("TCP 探测失败，未进入协议解析分支");
                return;
            }

            Assert.IsFalse(ok);
            Assert.AreEqual(DeviceStatusType.Error, device.StatusType);
        }

        /// <summary>端口不可达 → Offline，且不调用协议。</summary>
        [TestMethod]
        public async Task ConnectAsync_WhenPortClosed_ShouldBeOffline () {
            var protocol = new FakeProtocol { ConnectResult = true };
            var resolver = new FakeProtocolResolver { ProtocolToReturn = protocol };
            var svc = new DeviceService(resolver, new FakeDeviceRepository());

            // 建议使用几乎不会开放的端口
            var device = CreateDevice("127.0.0.1", 1);
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.IsFalse(device.IsConnected);
            Assert.AreEqual(DeviceStatusType.Offline, device.StatusType);
            Assert.AreEqual(0, protocol.ConnectCallCount);
        }

        /// <summary>从插件目录加载 Modbus 程序集。</summary>
        [TestMethod]
        public void ProtocolResolver_LoadPlugin_ShouldFindModbusTcp () {
            string testsBin = System.AppDomain.CurrentDomain.BaseDirectory;
            string pluginDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(testsBin, @"..\..\..\Plugin.ModbusTcp\bin\Debug"));

            Assert.IsTrue(
                System.IO.Directory.Exists(pluginDir),
                "插件目录不存在: " + pluginDir);

            string[] files = System.IO.Directory.GetFiles(pluginDir, "Plugin.*.dll");
            Assert.IsTrue(files.Length > 0, "目录下没有 Plugin.*.dll: " + pluginDir);

            var resolver = new ProtocolResolver();
            resolver.LoadFromFolder(pluginDir);

            var names = resolver.GetProtocolNames();
            Assert.IsTrue(names.Count > 0, "未加载到任何插件: " + pluginDir);
            Assert.IsTrue(names.Contains("Modbus TCP"));
        }

        /// <summary>构造带 ProtocolSettingsJson 的测试设备。</summary>
        /// <summary>构造测试设备（站号走 StationNo）。</summary>
        private static DeviceInfo CreateDevice (string ip, int port) {
            return new DeviceInfo {
                Name = "T1",
                Protocol = "Modbus TCP",
                Ip = ip,
                Port = port,
                StationNo = 1
            };
        }
    }
}