using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public partial class ProfileControl : UserControl
    {
        public ProfileControl(string name, string cccd)
        {
            InitializeComponent();
            SetupUI(name, cccd);
        }

        private void SetupUI(string name, string cccd)
        {
            this.BackColor = ColorPalette.ContentBg;
            lblTitle.ForeColor = ColorPalette.TextPrimary;
            lblSubtitle.ForeColor = ColorPalette.TextSecondary;
            
            lblCccdVal.Text = string.IsNullOrEmpty(cccd) ? "Chưa cập nhật" : cccd;
            lblName.Text = string.IsNullOrEmpty(name) ? "Tài khoản học viên" : name;
            
            panelInfo.BackColor = ColorPalette.CardBg;
            btnChangePass.BackColor = ColorPalette.Primary;
            btnChangePass.ForeColor = Color.White;
        }

        private void panelAvatar_Paint(object sender, PaintEventArgs e)
        {
            // Draw a circular avatar placeholder
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(ColorPalette.BgGradientStart))
            {
                e.Graphics.FillEllipse(brush, new Rectangle(0, 0, panelAvatar.Width - 1, panelAvatar.Height - 1));
            }
            TextRenderer.DrawText(e.Graphics, "YB", new Font("Segoe UI", 24F, FontStyle.Bold), 
                new Rectangle(0, 0, panelAvatar.Width, panelAvatar.Height), 
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
