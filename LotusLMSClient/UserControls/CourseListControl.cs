using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Services;
using LotusLMSClient.Models;
using System.Collections.Generic;

namespace LotusLMSClient.UserControls
{
    public partial class CourseListControl : UserControl
    {
        private readonly CourseService _courseService;
        private List<Course> _courses;

        public CourseListControl(AuthService authService)
        {
            InitializeComponent();
            _courseService = new CourseService(authService.ApiService, authService);
            SetupUI();
            LoadCoursesAsync();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            lblTitle.ForeColor = ColorPalette.TextPrimary;

            // Buttons styling
            btnAll.BackColor = ColorPalette.Primary;
            btnAll.ForeColor = Color.White;
            btnLearning.ForeColor = ColorPalette.TextSecondary;
            btnCompleted.ForeColor = ColorPalette.TextSecondary;

            // Search box styling
            txtSearch.BackColor = ColorPalette.CardBg;
            txtSearch.ForeColor = ColorPalette.TextPrimary;
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
        }

        private async void LoadCoursesAsync()
        {
            flowLayoutCourses.Controls.Clear();
            Label lblLoading = new Label { Text = "Đang tải dữ liệu...", AutoSize = true, Font = new Font("Segoe UI", 12F) };
            flowLayoutCourses.Controls.Add(lblLoading);

            _courses = await _courseService.GetUserCoursesAsync();

            flowLayoutCourses.Controls.Clear();
            foreach (var course in _courses)
            {
                RenderCourseCard(course);
            }
        }

        private void RenderCourseCard(Course course)
        {
            Panel card = new Panel
            {
                Width = 320,
                Height = 250,
                BackColor = ColorPalette.CardBg,
                Margin = new Padding(15)
            };

            // Image placeholder
            PictureBox pic = new PictureBox
            {
                BackColor = Color.LightGray,
                Width = 220,
                Height = 120,
                Dock = DockStyle.Top
            };

            Label title = new Label
            {
                Text = course.Name,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(10, 130),
                AutoSize = true,
                MaximumSize = new Size(400, 0)
            };


            ProgressBar progress = new ProgressBar
            {
                Value = (int)course.ProgressPercent,
                Location = new Point(10, 190),
                Width = 200,
                Height = 10
            };

            Button btnAction = new Button
            {
                Text = course.Status == "Completed" ? "Xem lại" : "Tiếp tục học ngày hôm nay",
                BackColor = course.Status == "Completed" ? ColorPalette.Success : ColorPalette.Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, 210),
                Width = 200,
                Height = 30
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Click += (s, e) => MessageBox.Show($"Mở khóa học: {course.Name}");

            card.Controls.Add(btnAction);
            card.Controls.Add(progress);
            card.Controls.Add(title);
            card.Controls.Add(pic);

            flowLayoutCourses.Controls.Add(card);
        }
    }
}
