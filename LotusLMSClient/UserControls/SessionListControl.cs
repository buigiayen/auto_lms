using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public partial class SessionListControl : UserControl
    {
        public SessionListControl()
        {
            InitializeComponent();
            SetupUI();
            PopulateDummyData();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            lblTitle.ForeColor = ColorPalette.TextPrimary;
            lblSubtitle.ForeColor = ColorPalette.TextSecondary;
            
            dgvSessions.BackgroundColor = ColorPalette.CardBg;
            dgvSessions.BorderStyle = BorderStyle.None;
            dgvSessions.EnableHeadersVisualStyles = false;
            dgvSessions.ColumnHeadersDefaultCellStyle.BackColor = ColorPalette.PrimaryDark;
            dgvSessions.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvSessions.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            dgvSessions.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            dgvSessions.RowTemplate.Height = 40;
        }

        private void PopulateDummyData()
        {
            dgvSessions.Rows.Add("28/03/2026", "08:00 - 11:30", "Phòng 102", "Lý thuyết GPLX B2 - Đạo đức người lái", "Nguyễn Văn A", "Chưa bắt đầu");
            dgvSessions.Rows.Add("29/03/2026", "13:30 - 17:00", "Sân Tập A", "Thực hành Lái xe Cơ bản", "Phạm Văn B", "Sắp diễn ra");
            dgvSessions.Rows.Add("02/04/2026", "08:00 - 10:00", "Phòng Máy 1", "Học luật trên App Simulator", "Lê Văn C", "Chưa bắt đầu");
            dgvSessions.Rows.Add("05/04/2026", "14:00 - 16:30", "Offline", "Hướng dẫn kiểm tra kỹ thuật xe", "Trần Văn D", "Đã điểm danh");
        }
    }
}
