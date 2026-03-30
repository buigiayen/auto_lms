using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Models;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.UserControls
{
    public class CourseDetailControl : UserControl
    {
        private Course _course;
        public Action OnBack { get; set; }
        public Action<Course, Lesson> OnLessonSelected { get; set; }

        public CourseDetailControl(Course course)
        {
            _course = course;
            SetupUI();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Padding = new Padding(30);

            // Back button
            Button btnBack = new Button
            {
                Text = "← Quay lại",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = ColorPalette.Primary,
                BackColor = ColorPalette.ContentBg,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0, MouseOverBackColor = ColorPalette.SidebarHover },
                AutoSize = true,
                Cursor = Cursors.Hand,
                Location = new Point(30, 30)
            };
            btnBack.Click += (s, e) => OnBack?.Invoke();
            this.Controls.Add(btnBack);

            // Title
            Label lblTitle = new Label
            {
                Text = _course.Name,
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                Location = new Point(30, btnBack.Bottom + 20)
            };
            this.Controls.Add(lblTitle);

            // Instructor
            Label lblInstructor = new Label
            {
                Text = "Giảng viên: " + _course.InstructorName,
                Font = new Font("Segoe UI", 12F),
                ForeColor = ColorPalette.TextSecondary,
                AutoSize = true,
                Location = new Point(30, lblTitle.Bottom + 10)
            };
            this.Controls.Add(lblInstructor);

            // Progress text
            Label lblProgressText = new Label
            {
                Text = $"Tiến độ: {_course.CompletedLessons}/{_course.TotalLessons} bài học hoàn thành",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.DarkGreen,
                AutoSize = true,
                Location = new Point(30, lblInstructor.Bottom + 20)
            };
            this.Controls.Add(lblProgressText);

            // Progress bar
            ProgressBar progressBar = new ProgressBar
            {
                Value = (int)_course.ProgressPercent,
                Width = 400,
                Height = 15,
                Location = new Point(30, lblProgressText.Bottom + 10)
            };
            this.Controls.Add(progressBar);

            // Lessons Title
            Label lblLessonsTitle = new Label
            {
                Text = "Nội dung khóa học",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                Location = new Point(30, progressBar.Bottom + 40)
            };
            this.Controls.Add(lblLessonsTitle);

            // FlowLayout for Lessons
            FlowLayoutPanel flowLessons = new FlowLayoutPanel
            {
                Location = new Point(30, lblLessonsTitle.Bottom + 20),
                Width = this.Width - 60,
                Height = this.Height - lblLessonsTitle.Bottom - 50,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            if (_course.Lessons == null || _course.Lessons.Count == 0)
            {
                Label lblNoLessons = new Label
                {
                    Text = "Chưa có bài học nào được cập nhật.",
                    Font = new Font("Segoe UI", 12F, FontStyle.Italic),
                    ForeColor = Color.Gray,
                    AutoSize = true
                };
                flowLessons.Controls.Add(lblNoLessons);
            }
            else
            {
                foreach (var lesson in _course.Lessons)
                {
                    Panel pnlLesson = CreateLessonRow(lesson);
                    flowLessons.Controls.Add(pnlLesson);
                }
            }

            this.Controls.Add(flowLessons);

            // Resize handle so items expand
            this.Resize += (s, e) =>
            {
                foreach (Control c in flowLessons.Controls)
                {
                    if (c is Panel pnl)
                    {
                        pnl.Width = flowLessons.ClientSize.Width - 10;
                    }
                }
            };
        }

        private Panel CreateLessonRow(Lesson lesson)
        {
            Panel p = new Panel
            {
                Width = this.ClientSize.Width > 150 ? this.ClientSize.Width - 150 : 800,
                Height = 60,
                BackColor = ColorPalette.CardBg,
                Margin = new Padding(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };

            // Icon/Status
            Label lblIcon = new Label
            {
                Text = lesson.IsCompleted ? "✅" : (lesson.ContentType == "Video" ? "▶️" : "📄"),
                Font = new Font("Segoe UI", 14F),
                Location = new Point(15, 15),
                AutoSize = true
            };
            p.Controls.Add(lblIcon);

            // Title
            Label lblTitle = new Label
            {
                Text = $"Bài {lesson.Id}: {lesson.Title}",
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                ForeColor = ColorPalette.TextPrimary,
                Location = new Point(50, 15),
                AutoSize = true
            };
            if (lesson.IsCompleted)
            {
                lblTitle.ForeColor = Color.DarkOliveGreen;
                lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Strikeout);
            }
            p.Controls.Add(lblTitle);

            // Action Button
            Button btnAction = new Button
            {
                Text = lesson.IsCompleted ? "Xem lại" : "Vào học",
                BackColor = lesson.IsCompleted ? Color.LightGray : ColorPalette.Primary,
                ForeColor = lesson.IsCompleted ? Color.Black : Color.White,
                FlatStyle = FlatStyle.Flat,
                Width = 100,
                Height = 35,
                Location = new Point(p.Width - 120, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnAction.FlatAppearance.BorderSize = 0;
            btnAction.Click += (s, e) =>
            {
                if (OnLessonSelected != null)
                {
                    OnLessonSelected(_course, lesson);
                }
                else
                {
                    MessageBox.Show($"Mở bài học: {lesson.Title}");
                }
            };
            p.Controls.Add(btnAction);

            // Row click acts as button click
            p.Click += (s, e) => btnAction.PerformClick();
            lblTitle.Click += (s, e) => btnAction.PerformClick();
            lblIcon.Click += (s, e) => btnAction.PerformClick();

            return p;
        }
    }
}
