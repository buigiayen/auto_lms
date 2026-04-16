using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;

namespace LotusLMSClient.Forms
{
    public class PracticeTestingForm : Form
    {
        private Panel panelHeader;
        private Label lblTitle;
        private Button btnClose;

        private FlowLayoutPanel panelContent;
        private Label lblQuestionInfo;
        private Label lblQuestionText;
        private FlowLayoutPanel panelOptions;
        private Button btnNext;

        private string _topicTitle;
        private string _mode;
        private int _currentQuestion = 1;
        private int _totalQuestions = 10; // Mock

        public PracticeTestingForm(string topicTitle, string mode)
        {
            _topicTitle = topicTitle;
            _mode = mode;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Đang luyện tập";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.White;

            // Header
            panelHeader = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = ColorPalette.PrimaryDark };
            lblTitle = new Label { Text = $"📝 Đang luyện: {_topicTitle} ({(_mode == "wrong" ? "Câu sai" : "Tất cả")})", ForeColor = Color.White, Font = new Font("Segoe UI", 11F, FontStyle.Bold), AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0) };
            btnClose = new Button { Text = "✕", ForeColor = Color.White, BackColor = Color.Transparent, FlatStyle = FlatStyle.Flat, Size = new Size(50, 55), Dock = DockStyle.Right, Font = new Font("Segoe UI", 13F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 0, 0);
            btnClose.Click += (s, e) => this.Close();
            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(btnClose);

            // Container for Question
            panelContent = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(30) };

            lblQuestionInfo = new Label { Text = $"Câu {_currentQuestion}/{_totalQuestions}", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = ColorPalette.Primary, AutoSize = true, Margin = new Padding(0, 0, 0, 15) };
            lblQuestionText = new Label { Text = "Nội dung câu hỏi sẽ hiển thị ở đây (mô phỏng)...", Font = new Font("Segoe UI", 14F), AutoSize = true, MaximumSize = new Size(this.Width - 60, 0), Margin = new Padding(0, 0, 0, 20) };
            
            panelOptions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0,0,0,30) };
            
            btnNext = new Button { Text = "Tiếp tục", Font = new Font("Segoe UI", 11F, FontStyle.Bold), BackColor = ColorPalette.Primary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(150, 40), Cursor = Cursors.Hand };
            btnNext.FlatAppearance.BorderSize = 0;
            btnNext.Click += BtnNext_Click;

            panelContent.Controls.Add(lblQuestionInfo);
            panelContent.Controls.Add(lblQuestionText);
            panelContent.Controls.Add(panelOptions);
            panelContent.Controls.Add(btnNext);

            this.Controls.Add(panelContent);
            this.Controls.Add(panelHeader);

            LoadMockQuestion();
            AttachDrag(panelHeader);
        }

        private void LoadMockQuestion()
        {
            lblQuestionInfo.Text = $"Câu {_currentQuestion}/{_totalQuestions}";
            lblQuestionText.Text = $"Câu hỏi {_currentQuestion}: Khái niệm \"Đường bộ\" được hiểu như thế nào là đúng?";
            
            panelOptions.Controls.Clear();
            var q1 = new RadioButton { Text = "Đường, cầu đường bộ.", Font = new Font("Segoe UI", 12F), AutoSize = true, Margin = new Padding(0, 0, 0, 10), Cursor = Cursors.Hand };
            var q2 = new RadioButton { Text = "Hầm đường bộ, bến phà đường bộ.", Font = new Font("Segoe UI", 12F), AutoSize = true, Margin = new Padding(0, 0, 0, 10), Cursor = Cursors.Hand };
            var q3 = new RadioButton { Text = "Cả hai ý trên đều đúng.", Font = new Font("Segoe UI", 12F), AutoSize = true, Margin = new Padding(0, 0, 0, 10), Cursor = Cursors.Hand };

            panelOptions.Controls.Add(q1);
            panelOptions.Controls.Add(q2);
            panelOptions.Controls.Add(q3);
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            // Logic to check answer, call API, submit answer...
            bool hasChecked = false;
            foreach (Control c in panelOptions.Controls)
            {
                if (c is RadioButton rb && rb.Checked)
                {
                    hasChecked = true;
                    break;
                }
            }

            if (!hasChecked)
            {
                MessageBox.Show("Vui lòng chọn một đáp án!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _currentQuestion++;
            if (_currentQuestion > _totalQuestions)
            {
                MessageBox.Show("Bạn đã hoàn thành chủ đề này!", "Chúc mừng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
                return;
            }
            LoadMockQuestion();
        }

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
