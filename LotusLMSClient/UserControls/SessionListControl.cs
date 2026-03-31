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
            
            // Premium DataGridView Styling
            dgvSessions.BackgroundColor = ColorPalette.CardBg;
            dgvSessions.GridColor = Color.FromArgb(230, 230, 230);
            dgvSessions.BorderStyle = BorderStyle.None;
            dgvSessions.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvSessions.EnableHeadersVisualStyles = false;
            
            // Header styling
            dgvSessions.ColumnHeadersDefaultCellStyle.BackColor = ColorPalette.PrimaryDark;
            dgvSessions.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvSessions.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            dgvSessions.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvSessions.ColumnHeadersHeight = 45;
            
            // Rows styling
            dgvSessions.DefaultCellStyle.BackColor = Color.White;
            dgvSessions.DefaultCellStyle.ForeColor = ColorPalette.TextPrimary;
            dgvSessions.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            dgvSessions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 245, 255);
            dgvSessions.DefaultCellStyle.SelectionForeColor = ColorPalette.Primary;
            
            // Alternating rows for readability
            dgvSessions.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            
            dgvSessions.RowTemplate.Height = 45;
            dgvSessions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
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
