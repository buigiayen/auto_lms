using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public partial class LogControl : UserControl
    {
        private RichTextBox _rtbLogs;
        private Panel _toolBar;
        private Button _btnClear;
        private Button _btnOpenFolder;
        private Label _lblTitle;
        private ComboBox _cbLogFiles;
        private bool _isViewingFile = false;

        public LogControl()
        {
            InitializeUI();
            LoadExistingLogs();
            AppLog.OnLogAdded += AppLog_OnLogAdded;
        }

        private void InitializeUI()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = ColorPalette.ContentBg;
            this.Padding = new Padding(20);

            _rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10F),
                ForeColor = ColorPalette.TextPrimary,
                Padding = new Padding(10)
            };

            _toolBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(0, 0, 0, 10)
            };

            _lblTitle = new Label
            {
                Text = "📜 Nhật ký hoạt động",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                Location = new Point(0, 10)
            };

            _btnOpenFolder = new Button
            {
                Text = "📂 Mở thư mục Log",
                FlatStyle = FlatStyle.Flat,
                Width = 150,
                Height = 35,
                BackColor = Color.White,
                ForeColor = ColorPalette.Primary,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 320, 10)
            };
            _btnOpenFolder.FlatAppearance.BorderColor = ColorPalette.Primary;
            _btnOpenFolder.Click += (s, e) => {
                try { Process.Start("explorer.exe", AppLog.GetLogDirectory()); } catch { }
            };

            _cbLogFiles = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 250,
                Location = new Point(10, 45),
                Font = new Font("Segoe UI", 9F)
            };
            _cbLogFiles.SelectedIndexChanged += CbLogFiles_SelectedIndexChanged;
            RefreshLogFileList();

            _btnClear = new Button
            {
                Text = "🧹 Xóa nhật ký",
                FlatStyle = FlatStyle.Flat,
                Width = 120,
                Height = 35,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(220, 53, 69),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 140, 10)
            };
            _btnClear.FlatAppearance.BorderColor = Color.FromArgb(220, 53, 69);
            _btnClear.Click += (s, e) => _rtbLogs.Clear();

            _toolBar.Controls.Add(_lblTitle);
            _toolBar.Controls.Add(_cbLogFiles);
            _toolBar.Controls.Add(_btnOpenFolder);
            _toolBar.Controls.Add(_btnClear);
            _toolBar.Height = 85; // Increase height for the combo box

            // Wrap RTB in a panel for border effect
            Panel pnlBorder = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = Color.FromArgb(230, 230, 230)
            };
            pnlBorder.Controls.Add(_rtbLogs);

            this.Controls.Add(pnlBorder);
            this.Controls.Add(_toolBar);
        }

        private void LoadExistingLogs()
        {
            foreach (var entry in AppLog.History)
            {
                AppendLog(entry);
            }
        }

        private void RefreshLogFileList()
        {
            _cbLogFiles.Items.Clear();
            _cbLogFiles.Items.Add("-- Nhật ký phiên hiện tại --");
            
            var files = AppLog.ListAvailableLogs();
            foreach (var f in files)
            {
                _cbLogFiles.Items.Add(Path.GetFileName(f));
            }
            _cbLogFiles.SelectedIndex = 0;
        }

        private void CbLogFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cbLogFiles.SelectedIndex == 0)
            {
                _isViewingFile = false;
                _rtbLogs.Clear();
                LoadExistingLogs();
            }
            else
            {
                _isViewingFile = true;
                string fileName = _cbLogFiles.SelectedItem.ToString();
                LoadLogFile(fileName);
            }
        }

        private void LoadLogFile(string fileName)
        {
            try
            {
                string path = Path.Combine(AppLog.GetLogDirectory(), fileName);
                if (File.Exists(path))
                {
                    _rtbLogs.Clear();
                    _rtbLogs.Text = File.ReadAllText(path);
                    _rtbLogs.SelectionStart = _rtbLogs.TextLength;
                    _rtbLogs.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                _rtbLogs.Text = "Lỗi khi đọc file: " + ex.Message;
            }
        }

        private void AppLog_OnLogAdded(object sender, LogEntry entry)
        {
            if (_isViewingFile) return; // Don't update UI if viewing a specific historical file

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => AppendLog(entry)));
            }
            else
            {
                AppendLog(entry);
            }
        }

        private void AppendLog(LogEntry entry)
        {
            if (_rtbLogs == null || _rtbLogs.IsDisposed) return;

            string time = $"[{entry.Timestamp:HH:mm:ss}] ";
            
            _rtbLogs.SelectionStart = _rtbLogs.TextLength;
            _rtbLogs.SelectionLength = 0;
            _rtbLogs.SelectionColor = Color.Gray;
            _rtbLogs.AppendText(time);

            _rtbLogs.SelectionColor = GetColorByLevel(entry.Level);
            _rtbLogs.AppendText($"[{entry.Level}] ");

            _rtbLogs.SelectionColor = ColorPalette.TextPrimary;
            _rtbLogs.AppendText($"{entry.Message}{Environment.NewLine}");
            
            _rtbLogs.ScrollToCaret();
        }

        private Color GetColorByLevel(string level)
        {
            return level switch
            {
                "INFO" => Color.FromArgb(13, 110, 253),
                "ERROR" => Color.FromArgb(220, 53, 69),
                "WARN" => Color.FromArgb(255, 193, 7),
                _ => ColorPalette.TextPrimary
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppLog.OnLogAdded -= AppLog_OnLogAdded;
            }
            base.Dispose(disposing);
        }
    }
}
