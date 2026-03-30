using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public partial class DashboardControl : UserControl
    {
        public DashboardControl(string name, string cccd)
        {
            InitializeComponent();
            SetupUI(name, cccd);
        }

        private void SetupUI(string name, string cccd)
        {
            this.BackColor = ColorPalette.ContentBg;
            lblWelcome.Text = string.IsNullOrEmpty(name) ? "Xin chào 👋!" : $"Xin chào 👋 {name}!";
            lblWelcome.ForeColor = ColorPalette.TextPrimary;

            // Setup Cards (Summary metrics)
            SetupSummaryCard(panelCard1, "📚 5 Khóa học", ColorPalette.Primary);
            SetupSummaryCard(panelCard2, "📅 3 Buổi học hôm nay", ColorPalette.Warning);
            SetupSummaryCard(panelCard3, "📝 2 Bài thi sắp tới", ColorPalette.Error);
            SetupSummaryCard(panelCard4, "✅ 85% Hoàn thành", ColorPalette.Success);
            
            // Populating UI with demo mock data
            PopulateOngoingCourses();
            PopulateUpcomingSessions();
        }

        private void SetupSummaryCard(Panel pnl, string text, Color accent)
        {
            pnl.BackColor = ColorPalette.CardBg;
            // Border could be handled in OnPaint for custom shadow/rounding
            Label lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = accent,
                AutoSize = true,
                Location = new Point(20, 25)
            };
            pnl.Controls.Add(lbl);
        }

        private void PopulateOngoingCourses()
        {
            // Flow layout mock setup
            for (int i = 0; i < 3; i++)
            {
                var pnlCourse = new Panel { Width = 200, Height = 100, BackColor = Color.White, Margin = new Padding(10) };
                
                var title = new Label { Text = "GPLX B2 - Module " + (i+1), Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true, Location = new Point(10, 10) };
                var progress = new ProgressBar { Value = (i+1)*25, Width = 180, Height = 15, Location = new Point(10, 60) };
                
                pnlCourse.Controls.Add(title);
                pnlCourse.Controls.Add(progress);
                flowLayoutPanelCourses.Controls.Add(pnlCourse);
            }
        }

        private void PopulateUpcomingSessions()
        {
            dgvLichHoc.Rows.Add("28/03/2026", "08:00", "Lý thuyết GPLX - Buổi 5");
            dgvLichHoc.Rows.Add("29/03/2026", "14:00", "Thực hành lái xe ngoài trời");
            dgvLichHoc.Rows.Add("30/03/2026", "08:00", "Thi thử lý thuyết B2");
            
            dgvLichHoc.BackgroundColor = ColorPalette.CardBg;
            dgvLichHoc.BorderStyle = BorderStyle.None;
            dgvLichHoc.EnableHeadersVisualStyles = false;
            dgvLichHoc.ColumnHeadersDefaultCellStyle.BackColor = ColorPalette.PrimaryDark;
            dgvLichHoc.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        }
    }
}
