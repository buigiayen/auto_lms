using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public partial class LessonViewerControl : UserControl
    {
        public LessonViewerControl()
        {
            InitializeComponent();
            SetupUI();
            InitializeAsync();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            panelSidebar.BackColor = Color.White;
            lblCourseTitle.ForeColor = ColorPalette.TextPrimary;
            btnBack.ForeColor = ColorPalette.Primary;
            btnNext.BackColor = ColorPalette.Primary;
            btnNext.ForeColor = Color.White;
            
            // Mock data for Sidebar Table of contents
            lstLessons.Items.Add("✅ Bài 1: Khái niệm cơ bản");
            lstLessons.Items.Add("✅ Bài 2: Quy tắc giao thông");
            lstLessons.Items.Add("▶️ Bài 3: Biển báo hiệu đường bộ");
            lstLessons.Items.Add("○ Bài 4: Hệ thống đèn tín hiệu");
            lstLessons.Items.Add("○ Bài 5: Sa hình");
        }

        async void InitializeAsync()
        {
            // Must initialize Environment before using WebView2 core features
            try {
                await webView.EnsureCoreWebView2Async(null);
                // Default blank or mock video URL for demo purposes
                // Since this is a demo, we use a placeholder youtube video
                webView.CoreWebView2.Navigate("https://www.youtube.com/embed/dQw4w9WgXcQ"); 
            } catch {
                // Ignore WebView2 init failures if running on a machine without WebView2 Runtime installed
            }
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            // Simulate go back logic (Handled by Parent form usually, but logging for now)
            Console.WriteLine("Quay lại danh sách bài học.");
        }
    }
}
