using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationDebuggingTools.Business.Device;
using CommunicationDebuggingTools.Business.Variable;
using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommunicationDebuggingTools.Tests {
    /// <summary>
    /// VariableService 读写单测（不连真实 PLC）。
    /// 通过预置 session 字典绕过 TCP 探测：直接使用已“连接”的 Fake 协议。
    /// </summary>
    [TestClass]
    public class VariableServiceReadWriteTests {
        private FakeProtocol _protocol;
        private DeviceService _devices;
        private VariableService _variables;
        private DeviceInfo _device;
        private VariableItem _variable;

        [TestInitialize]
        public void Setup () {
            _protocol = new FakeProtocol {
                ConnectResult = true,
                ReadResult = true,
                ReadValue = (short)55,
                WriteResult = true
            };

            var resolver = new FakeProtocolResolver { ProtocolToReturn = _protocol };
            _devices = new DeviceService(resolver, new FakeDeviceRepository());

            _device = new DeviceInfo {
                Name = "PLC1",
                Protocol = "Modbus TCP",
                Ip = "127.0.0.1",
                Port = 502,
                StationNo = 1
            };
        };
            _devices.Add(_device);

            // 不走 ConnectAsync（避免 TcpProbe）：直接标记已连接并挂上会话
            // 若 DeviceService 未暴露测试钩子，见下方「说明」
            AttachConnectedSession(_device.Id, _protocol);

            _variables = new VariableService(_devices, new FakeVariableRepository());

            _variable = new VariableItem {
                DeviceId = _device.Id,
                Name = "V1",
                Address = "40001",
                DataType = VariableDataType.Int16,
                Access = VariableAccess.ReadWrite
            };
            _variables.Add(_variable);
        }

        [TestMethod]
        public async Task ReadAsync_WhenOk_ShouldFillLastValue () {
            bool ok = await _variables.ReadAsync(_variable.Id, CancellationToken.None);

            Assert.IsTrue(ok);
            Assert.AreEqual((short)55, _variable.LastValue);
            Assert.AreEqual(DataQuality.Good, _variable.Quality);
            Assert.AreEqual(1, _protocol.ReadCallCount);
            Assert.AreEqual("40001", _protocol.LastReadRequest.Address);
        }

        [TestMethod]
        public async Task ReadAsync_WhenWriteOnly_ShouldFail () {
            _variable.Access = VariableAccess.WriteOnly;

            bool ok = await _variables.ReadAsync(_variable.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.AreEqual("只写变量不可读", _variable.LastError);
            Assert.AreEqual(0, _protocol.ReadCallCount);
        }

        [TestMethod]
        public async Task WriteAsync_WhenOk_ShouldUpdateLastValue () {
            bool ok = await _variables.WriteAsync(_variable.Id, (short)99, CancellationToken.None);

            Assert.IsTrue(ok);
            Assert.AreEqual((short)99, _variable.LastValue);
            Assert.AreEqual(1, _protocol.WriteCallCount);
            Assert.AreEqual((short)99, _protocol.LastWriteRequest.Value);
        }

        [TestMethod]
        public async Task WriteAsync_WhenReadOnly_ShouldFail () {
            _variable.Access = VariableAccess.ReadOnly;

            bool ok = await _variables.WriteAsync(_variable.Id, (short)1, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.AreEqual("只读变量不可写", _variable.LastError);
            Assert.AreEqual(0, _protocol.WriteCallCount);
        }

        [TestMethod]
        public async Task ReadAsync_WhenProtocolReadFails_ShouldBeBad () {
            _protocol.ReadResult = false;

            bool ok = await _variables.ReadAsync(_variable.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.AreEqual(DataQuality.Bad, _variable.Quality);
            Assert.IsFalse(string.IsNullOrEmpty(_variable.LastError));
        }

        [TestMethod]
        public async Task ReadAsync_WhenDeviceDisconnected_ShouldFail () {
            _devices.Disconnect(_device.Id);

            bool ok = await _variables.ReadAsync(_variable.Id, CancellationToken.None);

            Assert.IsFalse(ok);
            Assert.AreEqual("设备未连接", _variable.LastError);
            Assert.AreEqual(0, _protocol.ReadCallCount);
        }

        /// <summary>
        /// 将 Fake 协议挂到 DeviceService 会话表。
        /// 需要 DeviceService 提供测试可见的挂钩，或 InternalsVisibleTo + 内部方法。
        /// </summary>
        private void AttachConnectedSession (string deviceId, FakeProtocol protocol) {
            // 方式 A：若已有 public 测试辅助
            // _devices.AttachSessionForTest(deviceId, protocol);

            // 方式 B：最小侵入——在 DeviceService 增加 internal 方法（见下）
            _devices.AttachSessionForTest(deviceId, protocol);
        }
    }
}