using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Models;
using LotusLMSClient.Helpers;
using LotusLMSClient.Forms;
using System.Collections.Generic;
using LotusLMSClient.Services;

namespace LotusLMSClient.UserControls
{
    public partial class ExamDetailControl : UserControl
    {
        private readonly Course _course;
        private readonly RubricResult _result;
        private readonly CourseService _courseService;
        private SubjectDetails? _cachedDetails;
        public Action OnBack { get; set; }

        public ExamDetailControl(Course course, RubricResult result, CourseService courseService)
        {
            InitializeComponent();
            _course = course;
            _result = result;
            _courseService = courseService;
            SetupUI();
            BindData();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            
            btnBack.BackColor = ColorPalette.SidebarBg;
            btnBack.ForeColor = ColorPalette.SidebarActive;
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click += (s, e) => OnBack?.Invoke();

            lblDeadline.ForeColor = ColorPalette.TextPrimary;
            lblProgressTitle.ForeColor = ColorPalette.TextSecondary;
            lblPercent.ForeColor = ColorPalette.TextSecondary;
            lblTableTitle.ForeColor = ColorPalette.TextPrimary;

            // Summary Labels Styling
            lblSummaryPrefix.ForeColor = ColorPalette.TextPrimary;
            lblSummaryCurrent.ForeColor = Color.Red;
            lblSummaryMax.ForeColor = Color.Black;
            // lblSummaryStatus color set in BindData

            // DGV Styling
            dgvScores.BorderStyle = BorderStyle.None;
            dgvScores.BackgroundColor = Color.White;
            dgvScores.RowHeadersVisible = false;
            dgvScores.AllowUserToAddRows = false;
            dgvScores.AllowUserToResizeRows = false;
            dgvScores.GridColor = Color.FromArgb(232, 232, 232);
            dgvScores.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            
            dgvScores.EnableHeadersVisualStyles = false;
            dgvScores.ColumnHeadersHeight = 40;
            dgvScores.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 247, 255); // Light Blue Header
            dgvScores.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            dgvScores.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            dgvScores.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            
            dgvScores.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            dgvScores.DefaultCellStyle.SelectionBackColor = Color.FromArgb(245, 245, 245);
            dgvScores.DefaultCellStyle.SelectionForeColor = Color.Black;
            dgvScores.RowTemplate.Height = 45;

            InitializeColumns();
        }

        private void InitializeColumns()
        {
            dgvScores.Columns.Clear();
            dgvScores.Columns.Add(new DataGridViewTextBoxColumn { Name = "TaskName", HeaderText = "Tên nhiệm vụ", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            
            var scoreCol = new DataGridViewTextBoxColumn { Name = "Score", HeaderText = "Số giờ", Width = 150 };
            scoreCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvScores.Columns.Add(scoreCol);
            
            DataGridViewButtonColumn btnCol = new DataGridViewButtonColumn();
            btnCol.Name = "Action";
            btnCol.HeaderText = "Thao tác";
            btnCol.Text = "👁 Xem chi tiết";
            btnCol.UseColumnTextForButtonValue = true;
            btnCol.FlatStyle = FlatStyle.Flat;
            btnCol.Width = 150;
            btnCol.DefaultCellStyle.BackColor = Color.FromArgb(255, 111, 60);
            btnCol.DefaultCellStyle.ForeColor = Color.White;
            btnCol.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 111, 60);
            btnCol.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvScores.Columns.Add(btnCol);

            dgvScores.CellContentClick += dgvScores_CellContentClick;
            dgvScores.CellPainting += dgvScores_CellPainting;
        }

        private void BindData()
        {
            if (_result == null) return;

            // Update Deadline from EndDate (Unix timestamp) if available
            if (_result.EndDate.HasValue && _result.EndDate.Value > 0)
            {
                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(_result.EndDate.Value).LocalDateTime;
                lblDeadline.Text = $"Hoàn thành bài học trước {dt:dd/MM/yyyy}";
            }
            else
            {
                lblDeadline.Text = string.IsNullOrEmpty(_result.Deadline) ? "Chưa có hạn hoàn thành" : $"Hoàn thành bài học trước {_result.Deadline}";
            }
            
            // Progress from Expansion if available, otherwise root
            var expanded = _result.Expand?.ScoreByRubric;
            double cp = expanded?.Cp ?? _result.Cp ?? 0;
            progressTime.Value = (int)Math.Min(100, cp);
            lblPercent.Text = $"{cp:F1}%";

            double score = expanded?.Score ?? _result.P ?? _result.Score ?? 0;
            
            // DYNAMIC SCORES
            double maxScore = expanded?.Items?.Find(i => i.Name != null && i.Name.Contains("Tiến độ môn học"))?.MaxScore ?? 0;
            if (maxScore == 0) maxScore = _result.Rubric?.MaxScore ?? 25;
            
            double passScore = _result.Rubric?.PassScore ?? (maxScore * 0.7);

            bool isPassed = (expanded?.Passed == 1) || (score >= passScore);
            
            lblSummaryCurrent.Text = score.ToString("F2");
            lblSummaryMax.Text = $"/{maxScore}";
            lblSummaryStatus.Text = isPassed ? " - ✅ Hoàn thành" : " - ❌ Chưa đạt";
            lblSummaryStatus.ForeColor = isPassed ? ColorPalette.Success : Color.OrangeRed;

            // Center the flow layout manually
            if (this.Width > 0) flpSummary.Left = (this.Width - flpSummary.Width) / 2;
            dgvScores.Rows.Clear();
            var items = expanded?.Items;
            if (items != null)
            {
                foreach (var item in items)
                {
                    // Skip the main rubric root item if it has the same name as the course or is redundant
                    if (item.Name != null && item.Name.Contains("[2025]")) continue;

                    string displayName = item.Name ?? "N/A";
                    double scoreVal = item.Score ?? 0;
                    double maxVal = item.MaxScore ?? 0;
                    
                    // Fallback for items with missing max_score
                    if (maxVal == 0)
                    {
                        if (displayName == "Điểm kiểm tra") maxVal = 50; 
                        else if (displayName == "Xem giáo trình") maxVal = 10;
                        else if (displayName == "Ôn luyện") maxVal = 14;
                        else if (displayName == "Kiểm tra") maxVal = 1;
                    }

                    // Special formatting for "Điểm kiểm tra" to match "8/5" format if values are 80/50
                    string scoreDisplay = $"{scoreVal}/{maxVal}";
                    if (displayName == "Điểm kiểm tra" && scoreVal > 10)
                    {
                        scoreDisplay = $"{scoreVal / 10}/{maxVal / 10}";
                    }
                    
                    bool isChild = displayName == "Xem giáo trình" || displayName == "Ôn luyện" || displayName == "Kiểm tra";
                    string prefix = isChild ? "      " : (displayName == "Tiến độ môn học" ? "[-]  " : "");
                    
                    int rowIndex = dgvScores.Rows.Add(prefix + displayName, scoreDisplay);
                    
                    // Styling
                    var row = dgvScores.Rows[rowIndex];
                    row.Tag = item;
                    row.Cells["Score"].Style.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

                    if (isChild)
                    {
                        row.Cells["TaskName"].Style.ForeColor = Color.FromArgb(24, 144, 255); // Link Blue
                    }
                    else
                    {
                        row.Cells["TaskName"].Style.ForeColor = Color.FromArgb(89, 89, 89); // Group Gray
                        row.Cells["Action"] = new DataGridViewTextBoxCell 
                        { 
                            Value = "",
                            Style = new DataGridViewCellStyle { BackColor = Color.White, SelectionBackColor = Color.FromArgb(245, 245, 245) }
                        };
                    }
                }
            }
        }

        private async void dgvScores_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != dgvScores.Columns["Action"].Index) return;

            var row = dgvScores.Rows[e.RowIndex];
            var item = row.Tag as RubricItem;
            if (item == null) return;

            // Chỉ phản hồi button có text (nút cam)
            if (row.Cells[e.ColumnIndex].Value == null || string.IsNullOrWhiteSpace(row.Cells[e.ColumnIndex].Value.ToString()))
                return;

            try
            {
                this.Cursor = Cursors.WaitCursor;

                if (item.Name != null && item.Name.Contains("Ôn luyện"))
                {
                    if (_cachedDetails == null)
                    {
                        _cachedDetails = await _courseService.GetDetailLearningTimeAsync(_course.Iid);
                    }

                    if (_cachedDetails == null)
                    {
                        MessageBox.Show("Không thể tải thông tin chi tiết giáo trình.", "Lỗi");
                        return;
                    }

                    int pIid = 0;
                    try
                    {
                        string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(_cachedDetails);
                        var rootObj = Newtonsoft.Json.Linq.JObject.Parse(jsonStr);
                        var practiceTokens = rootObj.SelectTokens("$..[?(@.ntype == 'practice')]");
                        foreach (var token in practiceTokens)
                        {
                            if (token["iid"] != null)
                            {
                                pIid = (int)token["iid"];
                                break;
                            }
                        }
                    }
                    catch { }

                    if (pIid == 0)
                    {
                        MessageBox.Show("Không tìm thấy ID thực hành của khóa học này.", "Lỗi");
                        return;
                    }

                    using var practiceForm = new PracticeDetailsForm(_course, pIid, _courseService);
                    practiceForm.ShowDialog(this);
                    return;
                }

                // Fetch toàn bộ cây bài học (giống CourseDetailControl)
                if (_cachedDetails == null)
                {
                    _cachedDetails = await _courseService.GetDetailLearningTimeAsync(_course.Iid);
                }

                if (_cachedDetails == null)
                {
                    MessageBox.Show("Không thể tải thông tin chi tiết giáo trình.", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Mở popup với CourseDetailControl bên trong — có đầy đủ chức năng tự học
                using var detailsForm = new LessonDetailsForm(_course, _cachedDetails, _courseService);
                detailsForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        private void dgvScores_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && dgvScores.Columns[e.ColumnIndex].Name == "Score")
            {
                if (e.Value == null || e.Graphics == null) return;
                string text = e.Value.ToString() ?? string.Empty;
                if (!text.Contains("/")) return;

                e.PaintBackground(e.CellBounds, true);

                string[] parts = text.Split('/');
                string current = parts[0].Trim();
                string total = "/" + parts[1].Trim();

                Font font = e.CellStyle.Font ?? dgvScores.Font;
                
                // Measure sizes with NoPadding for tightest fit
                Size currentSize = TextRenderer.MeasureText(e.Graphics, current, font, Size.Empty, TextFormatFlags.NoPadding);
                Size totalSize = TextRenderer.MeasureText(e.Graphics, total, font, Size.Empty, TextFormatFlags.NoPadding);
                int totalWidth = currentSize.Width + totalSize.Width;

                // Center alignment calculation
                int startX = e.CellBounds.X + (e.CellBounds.Width - totalWidth) / 2;
                int y = e.CellBounds.Y + (e.CellBounds.Height - currentSize.Height) / 2;

                // Draw Current (Red/Orange)
                TextRenderer.DrawText(e.Graphics, current, font, new Point(startX, y), Color.FromArgb(255, 111, 60), TextFormatFlags.NoPadding);

                // Draw Total (Black) - Sit flush next to current
                TextRenderer.DrawText(e.Graphics, total, font, new Point(startX + currentSize.Width, y), Color.Black, TextFormatFlags.NoPadding);

                e.Handled = true;
            }
        }
    }
}
