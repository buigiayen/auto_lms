using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Services;
using LotusLMSClient.UserControls;

namespace LotusLMSClient.Forms
{
    /// <summary>
    /// Popup hiển thị danh sách bài học đầy đủ từ SubjectDetails,
    /// dùng lại SessionListControl giống CourseDetailControl.
    /// </summary>
    public class LessonDetailsForm : Form
    {
        private readonly Course _course;
        private readonly SubjectDetails _details;
        private readonly CourseService _courseService;

        private Panel panelHeader;
        private Label lblTitle;
        private Button btnClose;
        private CourseDetailControl _courseDetailControl;

        public LessonDetailsForm(Course course, SubjectDetails details, CourseService courseService)
        {
            _course = course;
            _details = details;
            _courseService = courseService;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = _course.Name;
            this.Size = new Size(1024, 768);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = ColorPalette.ContentBg;

            // ── Header ──────────────────────────────────────
            panelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = ColorPalette.PrimaryDark
            };

            lblTitle = new Label
            {
                Text = $"📋  Chi tiết: {_course.Name}",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0)
            };

            btnClose = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(50, 55),
                Dock = DockStyle.Right,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 0, 0);
            btnClose.Click += (s, e) => this.Close();

            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(btnClose);

            // Allow dragging via header
            AttachDrag(panelHeader);
            AttachDrag(lblTitle);

            // ── Course Detail Control (replaces SessionListControl) ────────
            _courseDetailControl = new CourseDetailControl(_course, _details, _courseService)
            {
                Dock = DockStyle.Fill
            };
            
            // "Quay lại" in the CourseDetailControl will just close this popup
            _courseDetailControl.OnBack = () => this.Close();

            this.Controls.Add(_courseDetailControl);
            this.Controls.Add(panelHeader);
            
            this.FormClosed += (s, e) =>
            {
                if (_courseDetailControl != null)
                {
                    _courseDetailControl.StopAutoStudy();
                }
            };
        }

        // ── Drag support ────────────────────────────────────
        private void AttachDrag(Control ctrl)
        {
            bool dragging = false;
            System.Drawing.Point dragStart = System.Drawing.Point.Empty;
            System.Drawing.Point formStart = System.Drawing.Point.Empty;

            ctrl.MouseDown += (s, e) => { dragging = true; dragStart = Cursor.Position; formStart = this.Location; };
            ctrl.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var d = System.Drawing.Point.Subtract(Cursor.Position, new Size(dragStart));
                this.Location = System.Drawing.Point.Add(formStart, new Size(d));
            };
            ctrl.MouseUp += (s, e) => dragging = false;
        }
    }
}
