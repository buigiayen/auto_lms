using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Services;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace LotusLMSClient.Forms
{
    public class PracticeDetailsForm : Form
    {
        private readonly Course _course;
        private readonly int _practiceIid;
        private readonly CourseService _courseService;

        private Panel panelHeader;
        private Label lblTitle;
        private Button btnClose;
        
        private Panel panelContent;
        private FlowLayoutPanel flowSubjects;

        public PracticeDetailsForm(Course course, int practiceIid, CourseService courseService)
        {
            _course = course;
            _practiceIid = practiceIid;
            _courseService = courseService;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Luyện theo chủ đề";
            this.Size = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = ColorPalette.ContentBg;

            // Header
            panelHeader = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = ColorPalette.PrimaryDark };
            lblTitle = new Label { Text = $"📋 Ôn luyện: {_course.Name}", ForeColor = Color.White, Font = new Font("Segoe UI", 11F, FontStyle.Bold), AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0) };
            btnClose = new Button { Text = "✕", ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(50, 55), Dock = DockStyle.Right, Font = new Font("Segoe UI", 13F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 0, 0);
            btnClose.Click += (s, e) => this.Close();
            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(btnClose);
            AttachDrag(panelHeader);
            AttachDrag(lblTitle);

            // Content
            panelContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            flowSubjects = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false };
            panelContent.Controls.Add(flowSubjects);

            this.Controls.Add(panelContent);
            this.Controls.Add(panelHeader);

            this.Load += async (s, e) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            flowSubjects.Controls.Clear();
            var lblLoading = new Label { Text = "Đang tải dữ liệu ôn luyện...", AutoSize = true, Font = new Font("Segoe UI", 12F) };
            flowSubjects.Controls.Add(lblLoading);

            var json = await _courseService.GetPracticeDetailAsync(_practiceIid, _course.Iid);

            flowSubjects.Controls.Clear();
            if (json == null)
            {
                flowSubjects.Controls.Add(new Label { Text = "Không thể lấy dữ liệu (API trả về null hoặc lỗi mạng).", ForeColor = Color.Red, AutoSize = true, Font = new Font("Segoe UI", 10F) });
                return;
            }
            
            BuildMockUiForTesting(json);
        }

        private void BuildMockUiForTesting(JObject data)
        {
            // Dump json to memory or file for debug
            try {
                System.IO.File.WriteAllText("practice_debug.json", data.ToString());
            } catch {}

            var rootRes = data["result"];
            
            int totalQuestions = 0;
            int right = 0;
            int wrong = 0;
            int remaining = 0;
            
            var examSco = rootRes?["_expand"]?["exam_sco"];
            if (examSco != null && examSco["nr_of_exam_questions"] != null)
                totalQuestions = examSco["nr_of_exam_questions"].Value<int>();
            
            remaining = totalQuestions;

            // In some LMS formats, we might have progress stats (like passed/failed). 
            // We use default 0 if not exist.

            var lblProgressBar = new Label { Text = "Tiến độ theo thời gian luyện ⏱: - %", Font = new Font("Segoe UI", 10F), AutoSize = true, Margin = new Padding(0, 0, 0, 20), ForeColor = Color.Gray };
            var lblHeaderTitle = new Label { Text = "Luyện theo chủ đề", Font = new Font("Segoe UI", 20F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 10) };
            
            // Rich Text for stats
            var pnlStats = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 20) };
            pnlStats.Controls.Add(new Label { Text = $"Số câu hỏi:", Font = new Font("Segoe UI", 11F), AutoSize = true});
            pnlStats.Controls.Add(new Label { Text = $"{totalQuestions}", Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(24, 144, 255), AutoSize = true, Margin = new Padding(0,0,10,0)});
            pnlStats.Controls.Add(new Label { Text = $"Đúng:", Font = new Font("Segoe UI", 11F), AutoSize = true});
            pnlStats.Controls.Add(new Label { Text = $"{right}", Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(82, 196, 26), AutoSize = true, Margin = new Padding(0,0,10,0)});
            pnlStats.Controls.Add(new Label { Text = $"Sai:", Font = new Font("Segoe UI", 11F), AutoSize = true});
            pnlStats.Controls.Add(new Label { Text = $"{wrong}", Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.Red, AutoSize = true, Margin = new Padding(0,0,10,0)});
            pnlStats.Controls.Add(new Label { Text = $"Chưa luyện:", Font = new Font("Segoe UI", 11F), AutoSize = true});
            pnlStats.Controls.Add(new Label { Text = $"{remaining}", Font = new Font("Segoe UI", 11F, FontStyle.Bold), AutoSize = true });

            flowSubjects.Controls.Add(lblProgressBar);
            flowSubjects.Controls.Add(lblHeaderTitle);
            flowSubjects.Controls.Add(pnlStats);

            bool hasTemplates = false;
            if (examSco != null && examSco["template_details"] != null && examSco["template_details"].HasValues)
            {
                var templates = examSco["template_details"][0]?["exercise_template"];
                if (templates is JArray arr && arr.Count > 0)
                {
                    hasTemplates = true;
                    foreach(var tmp in arr)
                    {
                        string name = tmp["name"]?.ToString() ?? "Chủ đề Không tên";
                        int secTotal = 0;
                        if (tmp["groups"] != null && tmp["groups"].HasValues && tmp["groups"][0]["nr_of_questions"] != null)
                        {
                            secTotal = tmp["groups"][0]["nr_of_questions"].Value<int>();
                        }
                        flowSubjects.Controls.Add(CreateSubjectCard(name, secTotal, 0, 0, secTotal));
                    }
                }
            }
            
            if (!hasTemplates)
            {
                // Fallback rendering
                string pName = rootRes?["name"]?.ToString() ?? "Ôn Luyện";
                flowSubjects.Controls.Add(CreateSubjectCard(pName, totalQuestions, 0, 0, totalQuestions));
            }
        }

        private Panel CreateSubjectCard(string title, int total, int right, int wrong, int remaining)
        {
            var pnl = new Panel { Width = panelContent.Width - 60, Height = 140, Margin = new Padding(0, 0, 0, 15), Padding = new Padding(15) };
            pnl.BackColor = Color.White;
            // Draw border via paint event
            pnl.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, pnl.ClientRectangle, Color.LightGray, ButtonBorderStyle.Solid);
            
            var lblTitle = new Label { Text = title, Font = new Font("Segoe UI", 13F, FontStyle.Regular), AutoSize = true, Location = new Point(15, 15) };
            
            var lblTotal = new Label { Text = $"Tổng số câu: {total}", Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(15, 45) };
            lblTotal.ForeColor = Color.Gray;

            // Stats group
            var pnlStats = new FlowLayoutPanel { Location = new Point(15, 70), AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            pnlStats.Controls.Add(new Label { Text = $"Đúng:", Font = new Font("Segoe UI", 10F), AutoSize = true, ForeColor=Color.Gray, Margin = new Padding(0)});
            pnlStats.Controls.Add(new Label { Text = $"{right}", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(82, 196, 26), AutoSize = true, Margin = new Padding(0,0,10,0)});
            pnlStats.Controls.Add(new Label { Text = $"Sai:", Font = new Font("Segoe UI", 10F), AutoSize = true, ForeColor=Color.Gray, Margin = new Padding(0)});
            pnlStats.Controls.Add(new Label { Text = $"{wrong}", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.Red, AutoSize = true, Margin = new Padding(0,0,10,0)});
            pnlStats.Controls.Add(new Label { Text = $"Chưa luyện:", Font = new Font("Segoe UI", 10F), AutoSize = true, ForeColor=Color.Gray, Margin = new Padding(0)});
            pnlStats.Controls.Add(new Label { Text = $"{remaining}", Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true });

            int luyencausai = wrong + remaining;

            var btnAll = new Button { Text = $"Luyện tất cả ({total})", Font = new Font("Segoe UI", 9.5F), BackColor = Color.White, ForeColor = Color.FromArgb(24, 144, 255), FlatStyle = FlatStyle.Flat, Location = new Point(15, 100), AutoSize = true, Cursor = Cursors.Hand };
            btnAll.FlatAppearance.BorderColor = Color.FromArgb(24, 144, 255);
            btnAll.Click += (s, e) => OpenTestingForm(title, "all");

            var btnWrong = new Button { Text = $"Luyện câu sai ({luyencausai})", Font = new Font("Segoe UI", 9.5F), BackColor = Color.White, ForeColor = Color.FromArgb(24, 144, 255), FlatStyle = FlatStyle.Flat, Location = new Point(btnAll.Right + 10, 100), AutoSize = true, Cursor = Cursors.Hand };
            btnWrong.FlatAppearance.BorderColor = Color.FromArgb(24, 144, 255);
            btnWrong.Click += (s, e) => OpenTestingForm(title, "wrong");

            pnl.Controls.Add(lblTitle);
            pnl.Controls.Add(lblTotal);
            pnl.Controls.Add(pnlStats);
            pnl.Controls.Add(btnAll);
            if(luyencausai > 0) pnl.Controls.Add(btnWrong);

            return pnl;
        }

        private void OpenTestingForm(string title, string mode)
        {
            var testForm = new PracticeTestingForm(title, mode);
            testForm.ShowDialog(this);
        }

        // ── Drag support ────────────────────────────────────
        private void AttachDrag(Control ctrl)
        {
            bool dragging = false;
            Point dragStart = Point.Empty;
            Point formStart = Point.Empty;

            ctrl.MouseDown += (s, e) => { dragging = true; dragStart = Cursor.Position; formStart = this.Location; };
            ctrl.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var d = Point.Subtract(Cursor.Position, new Size(dragStart));
                this.Location = Point.Add(formStart, new Size(d));
            };
            ctrl.MouseUp += (s, e) => dragging = false;
        }
    }
}
