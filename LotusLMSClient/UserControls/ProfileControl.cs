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

            // Optional: Give labels specific colors for better hierarchy
            lblCccd.ForeColor = ColorPalette.TextSecondary;
            lblPhone.ForeColor = ColorPalette.TextSecondary;
            lblCccdVal.ForeColor = ColorPalette.TextPrimary;
            lblPhoneVal.ForeColor = ColorPalette.TextPrimary;
        }

        private void panelAvatar_Paint(object sender, PaintEventArgs e)
        {
            // Draw a circular avatar placeholder
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, panelAvatar.Width - 1, panelAvatar.Height - 1);
            
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, 
                ColorPalette.BgGradientStart, ColorPalette.BgGradientEnd, 45f))
            {
                e.Graphics.FillEllipse(brush, rect);
            }

            // Draw initials
            string initials = "YB"; // Default for Bùi Gia Yên
            if (lblName.Text.Length >= 2)
            {
                var parts = lblName.Text.Split(' ');
                if (parts.Length >= 2) initials = (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpper();
            }

            TextRenderer.DrawText(e.Graphics, initials, new Font("Segoe UI", 24F, System.Drawing.FontStyle.Bold), 
                rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
