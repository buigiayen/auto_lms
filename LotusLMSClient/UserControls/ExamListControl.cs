using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Services;
using LotusLMSClient.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LotusLMSClient.UserControls
{
    public partial class ExamListControl : UserControl
    {
        private readonly CourseService _courseService;
        private List<Course> _courses;
        public Action<Course, RubricResult> OnExamDetailLoaded { get; set; }

        public ExamListControl(AuthService authService)
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

            btnAll.BackColor = ColorPalette.Primary;
            btnAll.ForeColor = Color.White;

            txtSearch.BackColor = ColorPalette.CardBg;
            txtSearch.ForeColor = ColorPalette.TextPrimary;
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
        }

        private async void LoadCoursesAsync()
        {
            flowLayoutCourses.Controls.Clear();
            Label lblLoading = new Label { Text = "Đang tải danh sách bài thi...", AutoSize = true, Font = new Font("Segoe UI", 12F) };
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
                Height = 220,
                BackColor = ColorPalette.CardBg,
                Margin = new Padding(15)
            };

            Label title = new Label
            {
                Text = course.Name,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(10, 20),
                AutoSize = true,
                MaximumSize = new Size(280, 60)
            };

            Label statusLabel = new Label
            {
                Text = "Trạng thái: " + (course.Status == "Completed" ? "Đã xong" : "Đang học"),
                Font = new Font("Segoe UI", 10F),
                Location = new Point(10, 90),
                ForeColor = course.Status == "Completed" ? ColorPalette.Success : ColorPalette.TextSecondary,
                AutoSize = true
            };

            ProgressBar progress = new ProgressBar
            {
                Value = (int)course.ProgressPercent,
                Location = new Point(10, 120),
                Width = 280,
                Height = 10
            };

            Button btnAction = new Button
            {
                Text = "Xem kết quả bài thi",
                BackColor = ColorPalette.Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, 150),
                Width = 280,
                Height = 35,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Click += async (s, e) => {
                btnAction.Enabled = false;
                btnAction.Text = "Đang lấy dữ liệu...";
                try
                {
                    var detail = await _courseService.GetExamCourseDetailAsync(course.Iid);
                    OnExamDetailLoaded?.Invoke(course, detail);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
                finally
                {
                    btnAction.Enabled = true;
                    btnAction.Text = "Xem kết quả bài thi";
                }
            };

            card.Controls.Add(btnAction);
            card.Controls.Add(progress);
            card.Controls.Add(statusLabel);
            card.Controls.Add(title);

            flowLayoutCourses.Controls.Add(card);
        }
    }
}
