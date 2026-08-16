using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MewtocolSim {
    /// <summary>
    /// MEWTOCOL-COM 从站：按松下 FP 系列真机帧格式模拟。
    /// RD/WD：区码+5位地址 起止各一次；寄存器字内小端。
    /// </summary>
    class Program {
        static readonly Dictionary<int, ushort> DT = new Dictionary<int, ushort>();
        static readonly Dictionary<string, bool> Contacts = new Dictionary<string, bool>();

        const int PORT = 9094;
        const string STATION = "01";

        static TcpListener _listener;
        static volatile bool _running = true;

        static void Main (string[] args) {
            Console.Title = "MEWTOCOL Slave (Real PLC)";
            Console.WriteLine("MEWTOCOL-COM 真机兼容从站");
            Console.WriteLine("端口: " + PORT + "  站号: " + STATION);
            Console.WriteLine("RD/WD: D#####D##### (5位×2)  字内: 小端\n");

            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                _running = false;
                try { _listener.Stop(); } catch { }
            };

            _listener = new TcpListener(IPAddress.Any, PORT);
            _listener.Start();
            Console.WriteLine("等待连接...\n");

            while (_running) {
                try {
                    TcpClient client = _listener.AcceptTcpClient();
                    Console.WriteLine("[" + Now() + "] 已连接: " + client.Client.RemoteEndPoint);
                    new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
                } catch { }
            }
        }

        static void HandleClient (TcpClient client) {
            NetworkStream stream = client.GetStream();
            byte[] buf = new byte[4096];
            var acc = new StringBuilder();
            try {
                while (client.Connected) {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    acc.Append(Encoding.ASCII.GetString(buf, 0, n));
                    string data = acc.ToString();
                    int cr;
                    while ((cr = data.IndexOf('\r')) >= 0) {
                        string raw = data.Substring(0, cr).Trim();
                        data = data.Substring(cr + 1);
                        acc.Clear();
                        acc.Append(data);
                        if (string.IsNullOrEmpty(raw)) continue;

                        W(ConsoleColor.DarkGray, "[" + Now() + "] ");
                        W(ConsoleColor.Cyan, "<<< ");
                        W(ConsoleColor.White, raw + "\n");

                        string resp = ProcessFrame(raw);
                        if (string.IsNullOrEmpty(resp)) continue;

                        byte[] outb = Encoding.ASCII.GetBytes(resp + "\r");
                        stream.Write(outb, 0, outb.Length);

                        W(ConsoleColor.DarkGray, "[" + Now() + "] ");
                        W(ConsoleColor.Green, ">>> ");
                        W(resp.IndexOf('!') >= 0 ? ConsoleColor.Red : ConsoleColor.Green, resp + "\n");
                        Console.WriteLine();
                    }
                }
            } catch { } finally {
                try { client.Close(); } catch { }
                Console.WriteLine("[" + Now() + "] 客户端断开\n");
            }
        }

        static string ProcessFrame (string frame) {
            if (frame.Length < 8 || frame[0] != '%')
                return MakeError(STATION, "0121");

            string station = frame.Substring(1, 2);
            if (station != STATION && station != "EE")
                return null;

            int sharp = frame.IndexOf('#');
            if (sharp < 0 || frame.Length < sharp + 3)
                return MakeError(station, "0121");

            // payload 用于 BCC：站号起至命令末（不含 BCC）
            string payload = frame.Substring(1, frame.Length - 3);
            string bccRecv = frame.Substring(frame.Length - 2);
            string bccCalc = BCC(payload);
            bool ok = string.Equals(bccRecv, bccCalc, StringComparison.OrdinalIgnoreCase);
            Log("BCC", "收=" + bccRecv + " 算=" + bccCalc + (ok ? " OK" : " 失败"));
            if (!ok)
                return MakeError(station, "0120"); // BCC 错误

            string cmd = frame.Substring(sharp + 1, frame.Length - sharp - 3);

            try {
                if (cmd.StartsWith("RD", StringComparison.Ordinal))
                    return DoReadData(station, cmd);
                if (cmd.StartsWith("WD", StringComparison.Ordinal))
                    return DoWriteData(station, cmd);
                if (cmd.StartsWith("RCS", StringComparison.Ordinal))
                    return DoReadContact(station, cmd);
                if (cmd.StartsWith("WCS", StringComparison.Ordinal))
                    return DoWriteContact(station, cmd);

                Log("错误", "不支持的命令");
                return MakeError(station, "0122");
            } catch (Exception ex) {
                Log("异常", ex.Message);
                return MakeError(station, "0121");
            }
        }

        /// <summary>
        /// 真机 RD：RDD00100D00101（区码+5位 起止各一次）
        /// </summary>
        static string DoReadData (string station, string cmd) {
            int start, end;
            if (!TryParseDataRange(cmd, 2, out start, out end))
                return MakeError(station, "0121");

            int count = end - start + 1;
            if (count < 1 || count > 500)
                return MakeError(station, "0323");

            Log("RD", "DT" + start + " .. DT" + end + " (" + count + " 字)");

            var data = new StringBuilder();
            for (int i = 0; i < count; i++) {
                ushort logical = GetDT(start + i);
                ushort wire = SwapBytes(logical); // 字内小端上线
                data.Append(wire.ToString("X4"));
                PrintReg(start + i, logical, wire);
            }
            return MakeNormal(station, "RD" + data);
        }

        /// <summary>
        /// 真机 WD：WDD00100D00101 + 每字4hex（字内小端）
        /// </summary>
        static string DoWriteData (string station, string cmd) {
            int start, end;
            if (!TryParseDataRange(cmd, 2, out start, out end))
                return MakeError(station, "0121");

            int count = end - start + 1;
            // "WD" + range(12) = 14 字符起为数据；range = D#####D##### = 12
            int dataPos = 2 + 12; // WD + D00100D00101
            if (cmd.Length < dataPos + count * 4) {
                Log("WD", "数据长度不足");
                return MakeError(station, "0121");
            }

            Log("WD", "DT" + start + " .. DT" + end + " (" + count + " 字)");
            string hex = cmd.Substring(dataPos);

            for (int i = 0; i < count; i++) {
                ushort wire;
                if (!ushort.TryParse(hex.Substring(i * 4, 4), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out wire))
                    return MakeError(station, "0121");

                ushort logical = SwapBytes(wire);
                DT[start + i] = logical;
                PrintReg(start + i, logical, wire);
            }
            return MakeNormal(station, "WD");
        }

        /// <summary>
        /// 解析 RD/WD 后的范围：D#####D#####（跳过命令字 2 字符后）。
        /// cmd 例：RDD00100D00101 / WDD00100D001010C00...
        /// </summary>
        static bool TryParseDataRange (string cmd, int cmdLen, out int start, out int end) {
            start = end = 0;
            // 期望：cmdLen 后为 D + 5digit + D + 5digit
            if (cmd.Length < cmdLen + 12)
                return false;
            if (cmd[cmdLen] != 'D' && cmd[cmdLen] != 'W')
                return false;
            if (cmd[cmdLen + 6] != cmd[cmdLen]) // 起止区码一致
                return false;

            if (!int.TryParse(cmd.Substring(cmdLen + 1, 5), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out start))
                return false;
            if (!int.TryParse(cmd.Substring(cmdLen + 7, 5), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out end))
                return false;
            if (end < start)
                return false;
            return true;
        }

        static string DoReadContact (string station, string cmd) {
            // RCS + 5 字符接点名，如 RCSR00100 / RCSR010A
            if (cmd.Length < 8)
                return MakeError(station, "0121");
            string key = cmd.Substring(3, 5).ToUpperInvariant();
            bool val = Contacts.ContainsKey(key) && Contacts[key];
            Log("RCS", key + " = " + (val ? "ON" : "OFF"));
            return MakeNormal(station, "RC" + (val ? "1" : "0"));
        }

        static string DoWriteContact (string station, string cmd) {
            if (cmd.Length < 9)
                return MakeError(station, "0121");
            string key = cmd.Substring(3, 5).ToUpperInvariant();
            char v = cmd[8];
            if (v != '0' && v != '1')
                return MakeError(station, "0121");
            Contacts[key] = v == '1';
            Log("WCS", key + " → " + (v == '1' ? "ON" : "OFF"));
            return MakeNormal(station, "WC");
        }

        static ushort GetDT (int addr) {
            ushort v;
            return DT.TryGetValue(addr, out v) ? v : (ushort)0;
        }

        static ushort SwapBytes (ushort w) {
            return (ushort)((w << 8) | (w >> 8));
        }

        static void PrintReg (int addr, ushort logical, ushort wire) {
            W(ConsoleColor.DarkGray, "  DT" + addr);
            W(ConsoleColor.White, " 逻辑=" + logical + " (0x" + logical.ToString("X4") + ")");
            W(ConsoleColor.DarkGray, " 线=0x" + wire.ToString("X4") + "\n");
        }

        static string MakeNormal (string station, string body) {
            string c = station + "$" + body;
            return "%" + c + BCC(c);
        }

        static string MakeError (string station, string code) {
            // 真机常见：! + 4 位错误码
            string c = station + "!" + code;
            return "%" + c + BCC(c);
        }

        static string BCC (string s) {
            byte x = 0;
            foreach (char ch in s)
                x ^= (byte)ch;
            return x.ToString("X2");
        }

        static string Now () {
            return DateTime.Now.ToString("HH:mm:ss.fff");
        }

        static void Log (string tag, string msg) {
            W(ConsoleColor.DarkGray, "  " + tag.PadRight(6) + msg + "\n");
        }

        static void W (ConsoleColor c, string t) {
            Console.ForegroundColor = c;
            Console.Write(t);
            Console.ResetColor();
        }
    }
}