using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.Forms
{
    public partial class LogViewerForm : Form
    {
        private RichTextBox _rtbLogs;
        private readonly string _logFilePath;
        private readonly int _itemIid;
        private readonly int _userIid;
        private Timer _refreshTimer;

        public LogViewerForm(int itemIid, int userIid)
        {
            _itemIid = itemIid;
            _userIid = userIid;
            _logFilePath = AppLog.GetLogFilePath(itemIid, userIid);
            
            InitializeComponent();
            SetupUI();
            LoadLogs();
            
            // Auto refresh log every 3 seconds while open
            _refreshTimer = new Timer { Interval = 3000 };
            _refreshTimer.Tick += (s, e) => LoadLogs();
            _refreshTimer.Start();
        }

        private void SetupUI()
        {
            this.Text = $"{_itemIid}-{_userIid}-{DateTime.Now:dd-MM-yyyy}.log";
            this.Size = new Size(700, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorPalette.ContentBg;
            this.Icon = SystemIcons.Information;

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

            Panel pnlBorder = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15),
                BackColor = ColorPalette.ContentBg
            };

            Panel pnlInner = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = Color.FromArgb(230, 230, 230)
            };
            pnlInner.Controls.Add(_rtbLogs);
            pnlBorder.Controls.Add(pnlInner);

            this.Controls.Add(pnlBorder);
        }

        private void LoadLogs()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    // Remember scroll position or just scroll to end
                    string content = File.ReadAllText(_logFilePath);
                    if (_rtbLogs.Text != content)
                    {
                        _rtbLogs.Text = content;
                        _rtbLogs.SelectionStart = _rtbLogs.TextLength;
                        _rtbLogs.ScrollToCaret();
                    }
                }
                else
                {
                    _rtbLogs.Text = "Chưa có dữ liệu nhật ký cho bài học này.";
                }
            }
            catch (Exception ex)
            {
                _rtbLogs.Text = "Lỗi khi đọc log: " + ex.Message;
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(684, 461);
            this.Name = "LogViewerForm";
            this.ResumeLayout(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Stop();
                _refreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
