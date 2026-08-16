using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MEWTOCOL_Slave {
    /// <summary>
    /// 简易 MEWTOCOL 从站：界面改 DT/接点 + 日志，后台 TCP 应答。
    /// </summary>
    public class MainForm : Form {
        readonly Dictionary<int, ushort> _dt = new Dictionary<int, ushort>();
        readonly Dictionary<string, bool> _contacts = new Dictionary<string, bool>();
        readonly object _lock = new object();

        TcpListener _listener;
        volatile bool _running;
        string _station = "01";
        int _port = 9094;

        TextBox _txtPort;
        TextBox _txtStation;
        Button _btnStart;
        Label _lblStatus;
        DataGridView _gridDt;
        DataGridView _gridContact;
        TextBox _log;
        TextBox _txtDtAddr;
        TextBox _txtDtVal;
        TextBox _txtCtKey;
        CheckBox _chkCtOn;

        public MainForm () {
            Text = "MEWTOCOL Slave 模拟器";
            Width = 920;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            SeedDefaults();
            RefreshDtGrid();
            RefreshContactGrid();
        }

        void BuildUi () {
            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            top.Controls.Add(new Label { Text = "端口", Left = 8, Top = 12, AutoSize = true });
            _txtPort = new TextBox { Text = "9094", Left = 44, Top = 8, Width = 70 };
            top.Controls.Add(_txtPort);
            top.Controls.Add(new Label { Text = "站号", Left = 130, Top = 12, AutoSize = true });
            _txtStation = new TextBox { Text = "01", Left = 168, Top = 8, Width = 40 };
            top.Controls.Add(_txtStation);
            _btnStart = new Button { Text = "启动监听", Left = 230, Top = 6, Width = 90, Height = 28 };
            _btnStart.Click += (s, e) => ToggleServer();
            top.Controls.Add(_btnStart);
            _lblStatus = new Label {
                Text = "未启动", Left = 330, Top = 12, AutoSize = true,
                ForeColor = Color.Gray
            };
            top.Controls.Add(_lblStatus);
            Controls.Add(top);

            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 360
            };

            var mid = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 480
            };

            // DT 区
            var dtPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var dtBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
            dtBar.Controls.Add(new Label { Text = "DT 寄存器", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            _txtDtAddr = new TextBox { Width = 60, Text = "100" };
            _txtDtVal = new TextBox { Width = 80, Text = "0" };
            var btnDt = new Button { Text = "写入 DT", Width = 80, Height = 26 };
            btnDt.Click += (s, e) => ApplyDt();
            var btnDtRef = new Button { Text = "刷新", Width = 60, Height = 26 };
            btnDtRef.Click += (s, e) => RefreshDtGrid();
            dtBar.Controls.Add(new Label { Text = "地址", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            dtBar.Controls.Add(_txtDtAddr);
            dtBar.Controls.Add(new Label { Text = "值", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            dtBar.Controls.Add(_txtDtVal);
            dtBar.Controls.Add(btnDt);
            dtBar.Controls.Add(btnDtRef);
            _gridDt = MakeGrid();
            _gridDt.Columns.Add("Addr", "地址");
            _gridDt.Columns.Add("Dec", "十进制");
            _gridDt.Columns.Add("Hex", "十六进制");
            _gridDt.Columns[0].Width = 80;
            _gridDt.Columns[1].Width = 100;
            _gridDt.Columns[2].Width = 100;
            _gridDt.Dock = DockStyle.Fill;
            _gridDt.CellEndEdit += GridDt_CellEndEdit;
            dtPanel.Controls.Add(_gridDt);
            dtPanel.Controls.Add(dtBar);
            mid.Panel1.Controls.Add(dtPanel);

            // 接点区
            var ctPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var ctBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
            ctBar.Controls.Add(new Label { Text = "接点 R", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            _txtCtKey = new TextBox { Width = 70, Text = "00100" };
            _chkCtOn = new CheckBox { Text = "ON", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
            var btnCt = new Button { Text = "写入接点", Width = 80, Height = 26 };
            btnCt.Click += (s, e) => ApplyContact();
            var btnCtRef = new Button { Text = "刷新", Width = 60, Height = 26 };
            btnCtRef.Click += (s, e) => RefreshContactGrid();
            ctBar.Controls.Add(new Label { Text = "编号", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            ctBar.Controls.Add(_txtCtKey);
            ctBar.Controls.Add(_chkCtOn);
            ctBar.Controls.Add(btnCt);
            ctBar.Controls.Add(btnCtRef);
            _gridContact = MakeGrid();
            _gridContact.Columns.Add("Key", "接点");
            _gridContact.Columns.Add("Val", "状态");
            _gridContact.Columns[0].Width = 100;
            _gridContact.Columns[1].Width = 80;
            _gridContact.Dock = DockStyle.Fill;
            _gridContact.CellEndEdit += GridContact_CellEndEdit;
            ctPanel.Controls.Add(_gridContact);
            ctPanel.Controls.Add(ctBar);
            mid.Panel2.Controls.Add(ctPanel);

            split.Panel1.Controls.Add(mid);

            _log = new TextBox {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro
            };
            var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            logPanel.Controls.Add(_log);
            logPanel.Controls.Add(new Label {
                Text = "通讯日志", Dock = DockStyle.Top, Height = 22
            });
            split.Panel2.Controls.Add(logPanel);

            // 给 Top 留位
            var host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(split);
            Controls.Add(host);
            host.BringToFront();
            top.BringToFront();

            FormClosing += (s, e) => StopServer();
        }

        static DataGridView MakeGrid () {
            return new DataGridView {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White
            };
        }

        void SeedDefaults () {
            lock (_lock) {
                _dt[100] = 0;
                _dt[200] = 0;
                _contacts["00100"] = false;
                _contacts["010A"] = false;
            }
        }

        void ToggleServer () {
            if (_running) {
                StopServer();
                return;
            }
            int port;
            if (!int.TryParse(_txtPort.Text.Trim(), out port) || port <= 0) {
                MessageBox.Show("端口无效");
                return;
            }
            string st = (_txtStation.Text ?? "01").Trim().PadLeft(2, '0');
            if (st.Length > 2) st = st.Substring(0, 2);
            _station = st;
            _port = port;
            _running = true;
            _btnStart.Text = "停止";
            _lblStatus.Text = "监听 " + _port + "  站号 " + _station;
            _lblStatus.ForeColor = Color.ForestGreen;
            _txtPort.Enabled = false;
            _txtStation.Enabled = false;
            Thread t = new Thread(ServerLoop) { IsBackground = true };
            t.Start();
            AppendLog("系统", "已启动 端口=" + _port + " 站号=" + _station);
        }

        void StopServer () {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
            if (IsHandleCreated) {
                BeginInvoke(new Action(() => {
                    _btnStart.Text = "启动监听";
                    _lblStatus.Text = "已停止";
                    _lblStatus.ForeColor = Color.Gray;
                    _txtPort.Enabled = true;
                    _txtStation.Enabled = true;
                }));
            }
            AppendLog("系统", "已停止监听");
        }

        void ServerLoop () {
            try {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                while (_running) {
                    try {
                        if (!_listener.Pending()) {
                            Thread.Sleep(50);
                            continue;
                        }
                        TcpClient client = _listener.AcceptTcpClient();
                        AppendLog("连接", client.Client.RemoteEndPoint.ToString());
                        Thread ct = new Thread(() => HandleClient(client)) { IsBackground = true };
                        ct.Start();
                    } catch {
                        if (!_running) break;
                    }
                }
            } catch (Exception ex) {
                AppendLog("错误", ex.Message);
                StopServer();
            }
        }

        void HandleClient (TcpClient client) {
            try {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 30000;
                var buf = new byte[4096];
                var acc = new StringBuilder();
                while (_running && client.Connected) {
                    int n;
                    try { n = stream.Read(buf, 0, buf.Length); }
                    catch { break; }
                    if (n <= 0) break;
                    acc.Append(Encoding.ASCII.GetString(buf, 0, n));
                    string all = acc.ToString();
                    int cr;
                    while ((cr = all.IndexOf('\r')) >= 0) {
                        string frame = all.Substring(0, cr).Trim('\n');
                        all = all.Substring(cr + 1);
                        if (string.IsNullOrEmpty(frame)) continue;
                        AppendLog("收", frame);
                        string resp = ProcessFrame(frame);
                        if (resp != null) {
                            byte[] outb = Encoding.ASCII.GetBytes(resp + "\r");
                            stream.Write(outb, 0, outb.Length);
                            stream.Flush();
                            AppendLog("发", resp);
                            BeginInvoke(new Action(() => {
                                RefreshDtGrid();
                                RefreshContactGrid();
                            }));
                        }
                    }
                    acc.Clear();
                    acc.Append(all);
                }
            } catch (Exception ex) {
                AppendLog("会话", ex.Message);
            } finally {
                try { client.Close(); } catch { }
                AppendLog("连接", "已断开");
            }
        }

        string ProcessFrame (string frame) {
            if (frame.Length < 8 || frame[0] != '%')
                return MakeError(_station, "0121");

            string station = frame.Substring(1, 2);
            if (station != _station && station != "EE")
                return null;

            int sharp = frame.IndexOf('#');
            if (sharp < 0 || frame.Length < sharp + 3)
                return MakeError(station, "0121");

            string payload = frame.Substring(1, frame.Length - 3);
            string bccRecv = frame.Substring(frame.Length - 2);
            if (!string.Equals(bccRecv, Bcc(payload), StringComparison.OrdinalIgnoreCase))
                return MakeError(station, "0120");

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
                return MakeError(station, "0122");
            } catch {
                return MakeError(station, "0121");
            }
        }

        string DoReadData (string station, string cmd) {
            int start, end;
            if (!TryParseDataRange(cmd, 2, out start, out end))
                return MakeError(station, "0121");
            int count = end - start + 1;
            if (count < 1 || count > 500)
                return MakeError(station, "0323");
            var data = new StringBuilder();
            lock (_lock) {
                for (int i = 0; i < count; i++) {
                    ushort logical = GetDt(start + i);
                    data.Append(SwapBytes(logical).ToString("X4"));
                }
            }
            AppendLog("RD", "DT" + start + ".." + end);
            return MakeNormal(station, "RD" + data);
        }

        string DoWriteData (string station, string cmd) {
            int start, end;
            if (!TryParseDataRange(cmd, 2, out start, out end))
                return MakeError(station, "0121");
            int count = end - start + 1;
            int dataPos = 2 + 12;
            if (cmd.Length < dataPos + count * 4)
                return MakeError(station, "0121");
            string hex = cmd.Substring(dataPos);
            lock (_lock) {
                for (int i = 0; i < count; i++) {
                    ushort wire;
                    if (!ushort.TryParse(hex.Substring(i * 4, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out wire))
                        return MakeError(station, "0121");
                    _dt[start + i] = SwapBytes(wire);
                }
            }
            AppendLog("WD", "DT" + start + ".." + end);
            return MakeNormal(station, "WD");
        }

        string DoReadContact (string station, string cmd) {
            if (cmd.Length < 8) return MakeError(station, "0121");
            string key = cmd.Substring(3, 5).ToUpperInvariant();
            bool val;
            lock (_lock) { val = _contacts.ContainsKey(key) && _contacts[key]; }
            return MakeNormal(station, "RC" + (val ? "1" : "0"));
        }

        string DoWriteContact (string station, string cmd) {
            if (cmd.Length < 9) return MakeError(station, "0121");
            string key = cmd.Substring(3, 5).ToUpperInvariant();
            char v = cmd[8];
            if (v != '0' && v != '1') return MakeError(station, "0121");
            lock (_lock) { _contacts[key] = v == '1'; }
            AppendLog("WCS", key + "=" + (v == '1' ? "ON" : "OFF"));
            return MakeNormal(station, "WC");
        }

        static bool TryParseDataRange (string cmd, int cmdLen, out int start, out int end) {
            start = end = 0;
            if (cmd.Length < cmdLen + 12) return false;
            if (cmd[cmdLen] != 'D' && cmd[cmdLen] != 'W') return false;
            if (cmd[cmdLen + 6] != cmd[cmdLen]) return false;
            if (!int.TryParse(cmd.Substring(cmdLen + 1, 5), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out start)) return false;
            if (!int.TryParse(cmd.Substring(cmdLen + 7, 5), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out end)) return false;
            return end >= start;
        }

        ushort GetDt (int addr) {
            ushort v;
            return _dt.TryGetValue(addr, out v) ? v : (ushort)0;
        }

        static ushort SwapBytes (ushort w) {
            return (ushort)((w << 8) | (w >> 8));
        }

        static string MakeNormal (string station, string body) {
            string c = station + "$" + body;
            return "%" + c + Bcc(c);
        }

        static string MakeError (string station, string code) {
            string c = station + "!" + code;
            return "%" + c + Bcc(c);
        }

        static string Bcc (string s) {
            byte x = 0;
            foreach (char ch in s) x ^= (byte)ch;
            return x.ToString("X2");
        }

        void ApplyDt () {
            int addr;
            int val;
            if (!int.TryParse(_txtDtAddr.Text.Trim(), out addr)) {
                MessageBox.Show("地址无效");
                return;
            }
            string raw = _txtDtVal.Text.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                if (!int.TryParse(raw.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val)) {
                    MessageBox.Show("值无效");
                    return;
                }
            } else if (!int.TryParse(raw, out val)) {
                MessageBox.Show("值无效");
                return;
            }
            lock (_lock) { _dt[addr] = (ushort)(val & 0xFFFF); }
            RefreshDtGrid();
            AppendLog("UI", "DT" + addr + " = " + (val & 0xFFFF));
        }

        void ApplyContact () {
            string key = (_txtCtKey.Text ?? "").Trim().ToUpperInvariant();
            if (key.Length == 0) return;
            if (key.Length < 5) key = key.PadLeft(5, '0');
            if (key.Length > 5) key = key.Substring(key.Length - 5);
            lock (_lock) { _contacts[key] = _chkCtOn.Checked; }
            RefreshContactGrid();
            AppendLog("UI", "R" + key + " = " + (_chkCtOn.Checked ? "ON" : "OFF"));
        }

        void GridDt_CellEndEdit (object sender, DataGridViewCellEventArgs e) {
            if (e.RowIndex < 0) return;
            var row = _gridDt.Rows[e.RowIndex];
            int addr;
            if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out addr)) return;
            string text = Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "0";
            int val;
            if (e.ColumnIndex == 2) {
                text = text.Replace("0x", "").Replace("0X", "");
                if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val))
                    return;
            } else if (e.ColumnIndex == 1) {
                if (!int.TryParse(text, out val)) return;
            } else return;
            lock (_lock) { _dt[addr] = (ushort)(val & 0xFFFF); }
            RefreshDtGrid();
        }

        void GridContact_CellEndEdit (object sender, DataGridViewCellEventArgs e) {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            var row = _gridContact.Rows[e.RowIndex];
            string key = Convert.ToString(row.Cells[0].Value) ?? "";
            if (key.StartsWith("R")) key = key.Substring(1);
            string v = (Convert.ToString(row.Cells[1].Value) ?? "").ToUpperInvariant();
            bool on = v == "ON" || v == "1" || v == "TRUE";
            lock (_lock) { _contacts[key] = on; }
            RefreshContactGrid();
        }

        void RefreshDtGrid () {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshDtGrid)); return; }
            List<KeyValuePair<int, ushort>> snap;
            lock (_lock) {
                snap = new List<KeyValuePair<int, ushort>>(_dt);
            }
            snap.Sort((a, b) => a.Key.CompareTo(b.Key));
            _gridDt.Rows.Clear();
            foreach (var kv in snap)
                _gridDt.Rows.Add("DT" + kv.Key, kv.Value.ToString(), "0x" + kv.Value.ToString("X4"));
        }

        void RefreshContactGrid () {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshContactGrid)); return; }
            List<KeyValuePair<string, bool>> snap;
            lock (_lock) {
                snap = new List<KeyValuePair<string, bool>>(_contacts);
            }
            snap.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            _gridContact.Rows.Clear();
            foreach (var kv in snap)
                _gridContact.Rows.Add("R" + kv.Key, kv.Value ? "ON" : "OFF");
        }

        void AppendLog (string tag, string msg) {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  [" + tag + "] " + msg + Environment.NewLine;
            if (!IsHandleCreated) return;
            try {
                BeginInvoke(new Action(() => {
                    _log.AppendText(line);
                    if (_log.TextLength > 50000)
                        _log.Text = _log.Text.Substring(_log.TextLength - 40000);
                }));
            } catch { }
        }
    }
}
