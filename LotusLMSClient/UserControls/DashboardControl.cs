using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Services;

namespace LotusLMSClient.UserControls
{
    public partial class DashboardControl : UserControl
    {
        private readonly CourseService _courseService;

        public DashboardControl(AuthService authService)
        {
            InitializeComponent();
            _courseService = new CourseService(authService.ApiService, authService);
            SetupUI(authService.CurrentName);
            LoadOngoingCoursesAsync();
        }

        private void SetupUI(string name)
        {
            BackColor = ColorPalette.ContentBg;
            lblWelcome.Text = string.IsNullOrEmpty(name) ? "Xin chào 👋!" : $"Xin chào 👋 {name}!";
            lblWelcome.ForeColor = ColorPalette.TextPrimary;
        }

        private async void LoadOngoingCoursesAsync()
        {
            flowLayoutPanelCourses.Controls.Clear();
            var lblLoading = new Label
            {
                Text = "Đang tải khóa học...",
                Font = new Font("Segoe UI", 11F),
                AutoSize = true,
                Margin = new Padding(10)
            };
            flowLayoutPanelCourses.Controls.Add(lblLoading);

            try
            {
                var courses = await _courseService.GetDashboardCoursesAsync();
                flowLayoutPanelCourses.Controls.Clear();

                if (courses.Count == 0)
                {
                    flowLayoutPanelCourses.Controls.Add(new Label
                    {
                        Text = "Không có khóa học",
                        Font = new Font("Segoe UI", 11F),
                        AutoSize = true,
                        Margin = new Padding(10)
                    });
                    return;
                }

                foreach (var course in courses)
                {
                    RenderCourseCard(course);
                }
            }
            catch
            {
                flowLayoutPanelCourses.Controls.Clear();
                flowLayoutPanelCourses.Controls.Add(new Label
                {
                    Text = "Không tải được dữ liệu khóa học",
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = ColorPalette.Error,
                    AutoSize = true,
                    Margin = new Padding(10)
                });
            }
        }

        private void RenderCourseCard(DashboardCourseItem course)
        {
            var panel = new Panel
            {
                Width = 220,
                Height = 120,
                BackColor = Color.White,
                Margin = new Padding(10)
            };

            var lblName = new Label
            {
                Text = course.Name,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(200, 0),
                Location = new Point(10, 10)
            };

            var lblCp = new Label
            {
                Text = $"CP: {course.CpPercent:0.##}%",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorPalette.Primary,
                AutoSize = true,
                Location = new Point(10, 65)
            };

            var lblP = new Label
            {
                Text = $"P: {course.PHours:0.##} giờ",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorPalette.TextSecondary,
                AutoSize = true,
                Location = new Point(10, 90)
            };

            panel.Controls.Add(lblName);
            panel.Controls.Add(lblCp);
            panel.Controls.Add(lblP);
            flowLayoutPanelCourses.Controls.Add(panel);
        }
    }
}
