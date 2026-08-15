using CommunicationDebuggingTools.Core.Enums;
using CommunicationDebuggingTools.Core.Attributes;
using CommunicationDebuggingTools.Core.Config;
using CommunicationDebuggingTools.Core.Interfaces;
using CommunicationDebuggingTools.Core.Models;
using CommunicationDebuggingTools.Core.Tools;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.ModbusTcp {
    /// <summary>
    /// Modbus TCP 插件入口：实现会话契约与共性报文读写。
    /// 底层 TCP/功能码见 <see cref="ModbusTcpSession"/>；地址与站号仅在本插件内解析。
    /// </summary>
    [ProtocolName("Modbus TCP")]
    public sealed class ModbusTcpProtocol : IProtocol {
        private readonly ModbusTcpSession _session = new ModbusTcpSession();
        private bool _disposed;

        /// <summary>当前是否已建立 TCP 会话。</summary>
        public bool IsConnected {
            get { return _session.IsConnected; }
        }

        /// <summary>协议显示名，须与设备 Protocol 字段一致。</summary>
        public string Name {
            get { return "Modbus TCP"; }
        }

        /// <summary>
        /// 使用共性连接上下文建连。站号只读 <see cref="ProtocolConnectionContext.StationNo"/>（映射为 UnitId）。
        /// </summary>
        public async Task<bool> ConnectAsync (
            ProtocolConnectionContext context,
            CancellationToken cancellationToken) {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            _session.Disconnect();
            if (string.IsNullOrWhiteSpace(context.Ip))
                return false;

            int station = context.StationNo;
            if (station < 0) station = 0;
            if (station > 255) station = 255;
            _session.UnitId = (byte)station;

            try {
                int timeout = context.TimeoutMs > 0 ? context.TimeoutMs : AppConfig.DefaultTimeoutMs;
                await _session.ConnectAsync(context.Ip, context.Port, timeout, cancellationToken);
                return true;
            } catch {
                _session.Disconnect();
                return false;
            }
        }

        /// <summary>断开并释放会话资源。</summary>
        public void Disconnect () {
            _session.Disconnect();
        }

        /// <summary>
        /// 按 <see cref="ProtocolDataMessage"/> 读取。
        /// 支持 Bool / 16·32·64 整型 / Float·Double / String。
        /// </summary>
        public Task<ProtocolDataMessage> ReadAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException("request");

            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();

                int addr = ModbusTcpSession.ParseAddress(request.Address);
                bool highFirst = request.WordOrder == WordOrder.HighWordFirst;

                switch (request.DataType) {
                    case VariableDataType.Bool: {
                        bool[] bits = _session.ReadCoils(addr, 1);
                        request.Value = bits != null && bits.Length > 0 && bits[0];
                        break;
                    }
                    case VariableDataType.Int16: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 1);
                        request.Value = (short)r[0];
                        break;
                    }
                    case VariableDataType.UInt16: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 1);
                        request.Value = r[0];
                        break;
                    }
                    case VariableDataType.Int32: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 2);
                        request.Value = ModbusTcpSession.RegistersToInt32(r[0], r[1], highFirst);
                        break;
                    }
                    case VariableDataType.UInt32: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 2);
                        request.Value = ModbusTcpSession.RegistersToUInt32(r[0], r[1], highFirst);
                        break;
                    }
                    case VariableDataType.Int64: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 4);
                        request.Value = ModbusTcpSession.RegistersToInt64(r, highFirst);
                        break;
                    }
                    case VariableDataType.UInt64: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 4);
                        request.Value = ModbusTcpSession.RegistersToUInt64(r, highFirst);
                        break;
                    }
                    case VariableDataType.Float: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 2);
                        request.Value = ModbusTcpSession.RegistersToFloat(r[0], r[1], highFirst);
                        break;
                    }
                    case VariableDataType.Double: {
                        ushort[] r = _session.ReadHoldingRegisters(addr, 4);
                        request.Value = ModbusTcpSession.RegistersToDouble(r, highFirst);
                        break;
                    }
                    case VariableDataType.String: {
                        request.Value = ReadStringValue(request, addr);
                        break;
                    }
                    default:
                        return Task.FromResult(Fail(request, "暂不支持: " + request.DataType));
                }

                request.Success = true;
                request.Quality = DataQuality.Good;
                request.ErrorMessage = "";
                return Task.FromResult(request);
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        /// <summary>
        /// 按 <see cref="ProtocolDataMessage"/> 写入。
        /// </summary>
        public Task<ProtocolDataMessage> WriteAsync (
            ProtocolDataMessage request,
            CancellationToken cancellationToken) {
            if (request == null)
                throw new ArgumentNullException("request");

            if (!_session.IsConnected)
                return Task.FromResult(Fail(request, "未连接"));

            try {
                cancellationToken.ThrowIfCancellationRequested();

                int addr = ModbusTcpSession.ParseAddress(request.Address);
                bool highFirst = request.WordOrder == WordOrder.HighWordFirst;

                switch (request.DataType) {
                    case VariableDataType.Bool:
                        _session.WriteSingleCoil(addr, Convert.ToBoolean(request.Value));
                        break;

                    case VariableDataType.Int16:
                    case VariableDataType.UInt16:
                        _session.WriteSingleRegister(addr, Convert.ToUInt16(request.Value));
                        break;

                    case VariableDataType.Int32: {
                        ushort hi, lo;
                        ModbusTcpSession.Int32ToRegisters(
                            Convert.ToInt32(request.Value), out hi, out lo, highFirst);
                        _session.WriteMultipleRegisters(addr, new ushort[] { hi, lo });
                        break;
                    }
                    case VariableDataType.UInt32: {
                        ushort hi, lo;
                        ModbusTcpSession.UInt32ToRegisters(
                            Convert.ToUInt32(request.Value), out hi, out lo, highFirst);
                        _session.WriteMultipleRegisters(addr, new ushort[] { hi, lo });
                        break;
                    }
                    case VariableDataType.Int64: {
                        ushort[] regs = new ushort[4];
                        ModbusTcpSession.Int64ToRegisters(
                            Convert.ToInt64(request.Value), regs, highFirst);
                        _session.WriteMultipleRegisters(addr, regs);
                        break;
                    }
                    case VariableDataType.UInt64: {
                        ushort[] regs = new ushort[4];
                        ModbusTcpSession.UInt64ToRegisters(
                            Convert.ToUInt64(request.Value), regs, highFirst);
                        _session.WriteMultipleRegisters(addr, regs);
                        break;
                    }
                    case VariableDataType.Float: {
                        ushort hi, lo;
                        ModbusTcpSession.FloatToRegisters(
                            Convert.ToSingle(request.Value), out hi, out lo, highFirst);
                        _session.WriteMultipleRegisters(addr, new ushort[] { hi, lo });
                        break;
                    }
                    case VariableDataType.Double: {
                        ushort[] regs = new ushort[4];
                        ModbusTcpSession.DoubleToRegisters(
                            Convert.ToDouble(request.Value), regs, highFirst);
                        _session.WriteMultipleRegisters(addr, regs);
                        break;
                    }
                    case VariableDataType.String:
                        WriteStringValue(request, addr);
                        break;

                    default:
                        return Task.FromResult(Fail(request, "暂不支持: " + request.DataType));
                }

                request.Success = true;
                request.ErrorMessage = "";
                return Task.FromResult(request);
            } catch (OperationCanceledException) {
                return Task.FromResult(Fail(request, "已取消"));
            } catch (Exception ex) {
                return Task.FromResult(Fail(request, ex.Message));
            }
        }

        /// <summary>读取字符串：按编码与 Length 计算寄存器数量。</summary>
        private string ReadStringValue (ProtocolDataMessage request, int addr) {
            var enc = ModbusTcpSession.ToEncoding(request.StringEncoding);
            int length = request.Length > 0 ? request.Length : 32;
            int maxBytes = enc.GetMaxByteCount(length);
            int regCount = (maxBytes + 1) / 2;
            if (regCount < 1)
                regCount = 1;

            ushort[] regs = _session.ReadHoldingRegisters(addr, regCount);
            byte[] bytes = ModbusTcpSession.RegistersToBytes(regs, request.ByteOrder);
            int n = 0;
            while (n < bytes.Length && bytes[n] != 0)
                n++;

            string s = enc.GetString(bytes, 0, n);
            if (s.Length > length)
                s = s.Substring(0, length);
            return s;
        }

        /// <summary>写入字符串：截断到 Length，不足补 0。</summary>
        private void WriteStringValue (ProtocolDataMessage request, int addr) {
            var enc = ModbusTcpSession.ToEncoding(request.StringEncoding);
            string value = request.Value != null ? request.Value.ToString() : "";
            int maxLength = request.Length > 0 ? request.Length : value.Length;
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);

            byte[] raw = enc.GetBytes(value);
            int regCount = (maxLength + 1) / 2;
            if (regCount < 1)
                regCount = 1;

            byte[] padded = new byte[regCount * 2];
            int copy = Math.Min(raw.Length, padded.Length);
            Buffer.BlockCopy(raw, 0, padded, 0, copy);

            ushort[] regs = ModbusTcpSession.BytesToRegisters(padded, request.ByteOrder);
            _session.WriteMultipleRegisters(addr, regs);
        }

        /// <summary>填充失败结果。</summary>
        private static ProtocolDataMessage Fail (ProtocolDataMessage request, string message) {
            request.Success = false;
            request.Quality = DataQuality.Bad;
            request.ErrorMessage = message ?? "";
            return request;
        }

        /// <summary>
        /// 从 ProtocolSettingsJson 解析 unitId；非法或缺失时返回 1。
        /// </summary>
        private static int ParseUnitId (string protocolSettingsJson) {
            int v = ExtraSettingsJsonHelper.GetInt(protocolSettingsJson, "unitId", 1);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        /// <summary>
        /// 探针：先做 Socket.Poll 检测 TCP 层，通了再 FC01 读线圈 0 验证 Modbus 通讯层。
        /// </summary>
        public Task<bool> PingAsync (System.Threading.CancellationToken cancellationToken) {
            // ① TCP 层
            if (!IsConnected) return Task.FromResult(false);
            // ② Modbus 协议层：FC01 读单个线圈（无副作用）
            try {
                cancellationToken.ThrowIfCancellationRequested();
                bool[] coils = _session.ReadCoils(0, 1);
                return Task.FromResult(coils != null && coils.Length > 0);
            } catch {
                return Task.FromResult(false);
            }
        }


        /// <summary>释放底层会话。</summary>
        public void Dispose () {
            if (_disposed)
                return;
            _disposed = true;
            _session.Dispose();
        }
    }
}