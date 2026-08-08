using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommunicationDebuggingTools.Tests {
    [TestClass]
    public class DeviceServiceConnectTests {
        [TestMethod]
        public async Task ConnectAsync_WhenProtocolOk_ShouldBeSuccess () {
            var protocol = new FakeProtocol { ConnectResult = true };
            var resolver = new FakeProtocolResolver { ProtocolToReturn = protocol };
            var repo = new FakeDeviceRepository();
            var svc = new DeviceService(resolver, repo);

            var device = new DeviceInfo
            {
                Name = "T1",
                Protocol = "Modbus TCP",
                Ip = "127.0.0.1",
                Port = 502
            };
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            Assert.IsTrue(ok);
            Assert.IsTrue(device.IsConnected);
            Assert.AreEqual(DeviceStatusType.Success, device.StatusType);
            Assert.AreEqual(1, protocol.ConnectCallCount);
        }

        [TestMethod]
        public async Task ConnectAsync_WhenProtocolFails_ShouldBeError () {
            var protocol = new FakeProtocol { ConnectResult = false };
            var resolver = new FakeProtocolResolver { ProtocolToReturn = protocol };
            var repo = new FakeDeviceRepository();
            var svc = new DeviceService(resolver, repo);

            var device = new DeviceInfo
            {
                Name = "T1",
                Protocol = "Modbus TCP",
                Ip = "192.168.0.10"
            };
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.IsFalse(device.IsConnected);
            Assert.AreEqual(DeviceStatusType.Error, device.StatusType);
        }

        [TestMethod]
        public async Task ConnectAsync_WhenProtocolMissing_ShouldBeError () {
            var resolver = new FakeProtocolResolver { ProtocolToReturn = null };
            var repo = new FakeDeviceRepository();
            var svc = new DeviceService(resolver, repo);

            var device = new DeviceInfo
            {
                Name = "T1",
                Protocol = "Modbus TCP"
            };
            svc.Add(device);

            bool ok = await svc.ConnectAsync(device.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.AreEqual(DeviceStatusType.Error, device.StatusType);
        }

        [TestMethod]
        public void ProtocolResolver_LoadPlugin_ShouldFindModbusTcp () {
            // 测试 dll 一般在: Tests\bin\Debug\
            // 插件一般在: Plugin.ModbusTcp\bin\Debug\
            string testsBin = System.AppDomain.CurrentDomain.BaseDirectory;
            string pluginDir = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(testsBin, @"..\..\..\Plugin.ModbusTcp\bin\Debug"));

            // 若你是 AnyCPU/Debug 输出不同，先 MessageBox/Assert 看路径是否存在
            Assert.IsTrue(
                System.IO.Directory.Exists(pluginDir),
                "插件目录不存在: " + pluginDir);

            string[] files = System.IO.Directory.GetFiles(pluginDir, "Plugin.*.dll");
            Assert.IsTrue(
                files.Length > 0,
                "目录下没有 Plugin.*.dll: " + pluginDir);

            var resolver = new CommunicationDebuggingTools.Business.Tools.ProtocolResolver();
            resolver.LoadFromFolder(pluginDir);

            var names = resolver.GetProtocolNames();
            Assert.IsTrue(names.Count > 0, "未加载到任何插件，检查 dll 路径: " + pluginDir);
            Assert.IsTrue(names.Contains("Modbus TCP"));
        }
    }
}