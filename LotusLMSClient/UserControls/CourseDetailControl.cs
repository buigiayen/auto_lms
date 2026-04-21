using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Services;
using LotusLMSClient.Forms;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LotusLMSClient.UserControls
{
    public partial class CourseDetailControl : UserControl
    {
        private readonly Course _course;
        private readonly SubjectDetails _rawData;

        private Panel _headerPanel;
        private Button _btnBack;
        private Label _lblCourseName;
        private Label _lblKpiInfo;
        private Panel _contentPanel;
        private Panel _nextSaveStrip;
        private Panel _saveCountdownBar;
        private SessionListControl _sessionList;

        private Panel _practiceProgressBar;
        private Panel _practiceProgressFill;
        private Label _lblPracticeProgress;
        private readonly CourseService _courseService;

        private System.Windows.Forms.Timer _countdownTickTimer;
        private int _secondsUntilNextSave;
        private bool _saveCycleRunning;
        private bool _autoStudyStoppedPermanently;
        private const int SaveIntervalSeconds = 15;
        private int _currentIntervalSeconds = SaveIntervalSeconds;
        private int _saveCallCount = 0;
        private dynamic _manualStudyItem = null;

        // Practice section
        private Panel _cardWrapper;
        private Panel _unifiedCard;
        private Panel _practicePanel;
        private Label _lblPracticeStats;
        private dynamic _practiceItem; // Store the actual practice object
        private int _practiceIid = 0;
        private int? _practiceExamTemplateIid;
        private int? _practiceBankIid;
        private int? _practiceExamScoIid;

        public Action OnBack { get; set; }

        public CourseDetailControl(Course course, SubjectDetails rawData, CourseService courseService)
        {
            InitializeComponent();
            _course = course;
            _rawData = rawData;
            _courseService = courseService;

            BuildUI();
        }

        private void BuildUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            this.Dock = DockStyle.Fill;

            BuildHeader();
            BuildContent();

            this.Controls.Add(_contentPanel);
            this.Controls.Add(_headerPanel);
        }

        private void BuildHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = ColorPalette.Primary,
                Padding = new Padding(12, 10, 12, 10)
            };
            
            Panel btnContainer = new Panel
            {
                Dock = DockStyle.Left,
                Width = 85,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 15, 0, 15)
            };

            _btnBack = new Button
            {
                Text = "← Quay lại",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            _btnBack.FlatAppearance.BorderSize = 1;
            _btnBack.FlatAppearance.BorderColor = Color.White;
            _btnBack.FlatAppearance.MouseOverBackColor = ColorPalette.PrimaryDark;
            _btnBack.Click += (s, e) => OnBack?.Invoke();
            
            btnContainer.Controls.Add(_btnBack);

            _lblCourseName = new Label
            {
                Text = $"📚  {_course.Name}",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0),
                AutoEllipsis = false
            };

            _lblKpiInfo = new Label
            {
                Text = "⏱ Thời gian đã học hôm nay: Đang tải...",
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
                Dock = DockStyle.Bottom,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0)
            };

            _headerPanel.Controls.Add(_lblCourseName);
            _headerPanel.Controls.Add(_lblKpiInfo);
            _headerPanel.Controls.Add(btnContainer);

            LoadInitialKpiAsync();
        }

        private async void LoadInitialKpiAsync()
        {
            var data = await _courseService.CheckTodayKpiTimeAsync(_course.Iid);
            UpdateKpiDisplay(data);
        }

        private void UpdateKpiDisplay(KpiData data)
        {
            if (this.IsDisposed || _lblKpiInfo == null) return;
            
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateKpiDisplay(data)));
                return;
            }

            _lblKpiInfo.Text = $"⏱ Thời gian đã học hôm nay: {data.FormattedProgress}";
            if (data.IsReached)
            {
                _lblKpiInfo.ForeColor = Color.FromArgb(100, 255, 100);
                _lblKpiInfo.Text += " ✅ Đã đủ KPI";
            }
            else
            {
                _lblKpiInfo.ForeColor = Color.FromArgb(220, 220, 220);
            }
        }

        private void BuildContent()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0), // No padding for the list
                BackColor = ColorPalette.ContentBg
            };

            BuildNextSaveStrip();

            // Khung chứa hợp nhất (Trắng, có bo viền)
            _unifiedCard = new Panel
            {
                Dock = DockStyle.Top, // Chuyển sang Top để co giãn theo nội dung
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Padding = new Padding(0) 
            };

            // Container bọc ngoài cùng của Card - Bật AutoScroll để cuộn toàn bộ nội dung
            _cardWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 15, 15, 20),
                BackColor = ColorPalette.ContentBg,
                AutoScroll = true 
            };

            _sessionList = new SessionListControl();
            _sessionList.Dock = DockStyle.Top; // Dock Top để xếp chồng lên nhau
            _sessionList.OnLessonSelected = (item) => 
            {
                _manualStudyItem = item;
                if (_countdownTickTimer == null || !_countdownTickTimer.Enabled)
                {
                    _nextSaveStrip.Visible = true;
                    _cardWrapper.AutoScrollPosition = new Point(0, 0); // Cuộn lên đầu khi xem giáo trình
                    StartAutoStudy();
                    MessageBox.Show($"Bắt đầu tự động học bài:\n{item.name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _cardWrapper.AutoScrollPosition = new Point(0, 0); // Cuộn lên đầu
                    MessageBox.Show($"Đã ưu tiên tự động học bài:\n{item.name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            BuildPracticePanel();

            // Sắp xếp: Thêm theo thứ tự để SessionList ở trên, Practice ở dưới
            _unifiedCard.Controls.Add(_practicePanel);
            _unifiedCard.Controls.Add(_sessionList);
            
            _unifiedCard.Paint += (s, e) => {
                using var pen = new Pen(Color.FromArgb(220, 220, 220), 1);
                var rect = _unifiedCard.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                e.Graphics.DrawRectangle(pen, rect);
            };

            _cardWrapper.Controls.Add(_unifiedCard);

            _contentPanel.Controls.Add(_nextSaveStrip);
            _contentPanel.Controls.Add(_cardWrapper);

            if (_rawData != null)
            {
                _sessionList.CurrentUserIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                _sessionList.DisplayTrackTimeData(_rawData);
                // Không tự động bắt đầu — chờ user chọn item
                _nextSaveStrip.Visible = false;

                FindPracticeInfo();
            }
            else
            {
                _nextSaveStrip.Visible = false;
            }
        }

        private void BuildNextSaveStrip()
        {
            _nextSaveStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(12, 3, 12, 3),
                BackColor = ColorPalette.ContentBg
            };

            _saveCountdownBar = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(230, 232, 235)
            };
            _saveCountdownBar.Paint += SaveCountdownBar_OnPaint;

            _nextSaveStrip.Controls.Add(_saveCountdownBar);
        }

        private void BuildPracticePanel()
        {
            _practicePanel = new Panel
            {
                Dock = DockStyle.Top, // Chuyển sang Top để nối tiếp danh sách bài học
                Height = 150,
                BackColor = Color.White,
                Padding = new Padding(30, 10, 30, 20),
                Visible = false
            };

            // Vẽ viền ngang mỏng để tách với danh sách bài học phía trên
            _practicePanel.Paint += (s, e) => {
                using var pen = new Pen(Color.FromArgb(235, 235, 235), 1);
                e.Graphics.DrawLine(pen, 15, 0, _practicePanel.Width - 15, 0);
            };

            Label title = new Label
            {
                Text = "📝  KẾ HOẠCH ÔN LUYỆN THEO CHỦ ĐỀ",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = ColorPalette.Primary,
                AutoSize = true,
                Location = new Point(30, 18)
            };
            _practicePanel.Controls.Add(title);

            _lblPracticeStats = new Label
            {
                Text = "📊  Câu hỏi: Đang tải...",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                Location = new Point(35, 48)
            };
            _practicePanel.Controls.Add(_lblPracticeStats);

            // Premium Progress Bar (Replacing Time Label)
            _lblPracticeProgress = new Label
            {
                Text = "⚡  Tiến độ: 0%",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = ColorPalette.TextPrimary,
                AutoSize = true,
                Location = new Point(35, 73)
            };
            _practicePanel.Controls.Add(_lblPracticeProgress);

            _practiceProgressBar = new Panel
            {
                Location = new Point(35, 95),
                Size = new Size(300, 8),
                BackColor = Color.FromArgb(235, 235, 235),
            };
            _practiceProgressFill = new Panel
            {
                Size = new Size(0, 8),
                BackColor = ColorPalette.Primary,
            };
            _practiceProgressBar.Controls.Add(_practiceProgressFill);
            _practicePanel.Controls.Add(_practiceProgressBar);

            FlowLayoutPanel btnFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Location = new Point(30, 115),
                BackColor = Color.Transparent
            };

            Button btnAuto = new Button
            {
                Text = "⚡  Auto Hoàn Thành",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(250, 173, 20),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 32),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 12, 0)
            };
            btnAuto.FlatAppearance.BorderSize = 0;
            btnAuto.Click += async (s, e) => await HandleAutoPracticeAsync();
            btnFlow.Controls.Add(btnAuto);

            Button btnOpen = new Button
            {
                Text = "🚀  Bắt Đầu Luyện",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = ColorPalette.Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 32),
                Cursor = Cursors.Hand
            };
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Click += (s, e) => HandleStudyPractice();
            btnFlow.Controls.Add(btnOpen);

            _practicePanel.Controls.Add(btnFlow);
        }

        public void ApplyStartMode(bool isPractice)
        {
            if (isPractice)
            {
                if (_cardWrapper != null)
                    _cardWrapper.AutoScrollPosition = new Point(0, _unifiedCard.Height);
            }
            else
            {
                if (_cardWrapper != null)
                    _cardWrapper.AutoScrollPosition = new Point(0, 0);
            }
        }

        private void FindPracticeInfo()
        {
            try
            {
                string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(_rawData);
                var rootObj = JObject.Parse(jsonStr);
                var practiceToken = rootObj.SelectToken("$..[?(@.ntype == 'practice')]");
                
                if (practiceToken != null && practiceToken["iid"] != null)
                {
                    _practiceIid = (int)practiceToken["iid"];
                    _practiceItem = practiceToken; // Save the item for later usage
                    _practicePanel.Visible = true;
                    
                    // Progress handled in LoadPracticeStatsAsync
                    Task.Run(() => LoadPracticeStatsAsync());
                }
            }
            catch { }
        }

        private bool TryGetLearningTimeText(JToken token, out string text)
        {
            text = string.Empty;
            try
            {
                int actual = token["actual_learning_time"]?.Value<int>() ?? 0;
                int min = token["min_learning_time"]?.Value<int>() ?? 0;
                if (min <= 0) return false;

                text = $"{FormatTime(actual)} / {FormatTime(min)}";
                if (actual >= min) text += " ✅ Đã đạt";
                return true;
            }
            catch { return false; }
        }

        private string FormatTime(int totalSeconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            return $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        private async Task LoadPracticeStatsAsync()
        {
            if (_practiceIid == 0) return;

            try
            {
                var practiceDetail = await _courseService.GetPracticeDetailAsync(_practiceIid, _course.Iid);
                if (practiceDetail == null) return;

                _practiceExamTemplateIid = practiceDetail.SelectToken("result._expand.exam_sco.template_details[0].exercise_template[0].exam_template_iid")?.Value<int?>();
                _practiceBankIid = practiceDetail.SelectToken("result._expand.exam_sco.question_banks[0]")?.Value<int?>();
                _practiceExamScoIid = practiceDetail.SelectToken("result._expand.exam_sco.iid")?.Value<int?>();

                // Trích xuất ri và rit động để đồng bộ đúng phiên làm việc
                string ri = practiceDetail.SelectToken("result._expand.exam_sco.ri")?.ToString();
                string rit = practiceDetail.SelectToken("result._expand.exam_sco.rit")?.ToString();

                int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                AppLog.Info($"[Diagnostic] Practice Detail: {practiceDetail.ToString(Newtonsoft.Json.Formatting.None)}", 0, userIid);

                if (_practiceExamScoIid.HasValue)
                {
                    var progress = await _courseService.GetSyncPracticeProgressAsync(_practiceIid, _practiceExamScoIid.Value, _course.Iid, ri, rit);
                    AppLog.Info($"[Diagnostic] Practice Progress: {progress?.ToString(Newtonsoft.Json.Formatting.None)}", 0, userIid);
                    
                    int? bankTotal = null;
                    if (_practiceExamScoIid.HasValue && _practiceBankIid.HasValue)
                    {
                        var bankStatus = await _courseService.GetQuestionBankStatusAsync(_practiceExamScoIid.Value, _practiceBankIid.Value, _course.Iid);
                        if (bankStatus != null && bankStatus["result"]?["questions"]?["result"] is JArray qArray)
                        {
                            bankTotal = qArray.Count;
                        }
                    }

                    UpdatePracticeStatsDisplay(practiceDetail, progress, bankTotal);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"Lỗi khi tải thống kê ôn luyện: {ex.Message}", 0, _courseService.AuthService?.CurrentUserIid ?? 0);
            }
        }

        private void UpdatePracticeStatsDisplay(JObject detail, JObject? progress, int? overrideTotal = null)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired) { this.Invoke(new Action(() => UpdatePracticeStatsDisplay(detail, progress, overrideTotal))); return; }

            // 1. Trích xuất dữ liệu gốc từ API
            var sco = detail.SelectToken("result._expand.exam_sco");
            int total = overrideTotal ?? sco?["nr_of_exam_questions"]?.Value<int>() ?? 0;
            int right = 0;
            int wrong = 0;

            // 2. Lấy số liệu thống kê chính thức nếu có
            if (sco != null)
            {
                var stats = sco["stats"];
                if (stats != null)
                {
                    right = stats["correct"]?.Value<int>() ?? 0;
                    wrong = stats["incorrect"]?.Value<int>() ?? 0;
                    if (stats["total"] != null) total = stats["total"].Value<int>();
                }
            }

            // 3. Fallback sang kết quả Sync nếu Stats chính thức chưa có hoặc bằng 0
            if (right == 0 && wrong == 0 && progress != null && progress["result"] != null)
            {
                var res = progress["result"];
                right = res["correct"]?.Value<int>() ?? 0;
                wrong = res["incorrect"]?.Value<int>() ?? 0;
                
                if (right == 0 && wrong == 0 && res["sync_data"] is JArray syncData)
                {
                    foreach (var item in syncData)
                    {
                        right += item["correct"]?.Value<int>() ?? 0;
                        wrong += item["incorrect"]?.Value<int>() ?? 0;
                    }
                }
                
                if (res["total"] != null && !overrideTotal.HasValue && total == 0) 
                {
                    total = res["total"].Value<int>();
                }
            }

            int finished = right + wrong;
            int remaining = Math.Max(0, total - finished);
            
            // Cập nhật text thống kê động
            _lblPracticeStats.Text = $"📊  Câu hỏi: Tổng {total} | Đúng {right} | Sai {wrong} | Còn {remaining}";

            // 4. Cập nhật Thanh tiến độ dựa trên số câu hỏi (Theo logic yêu cầu: ((Right + Wrong) * 100) / Total)
            if (total > 0)
            {
                int percentage = (finished * 100) / total;
                _lblPracticeProgress.Text = $"⚡  Tiến độ: {percentage}% ({finished}/{total})";
                _practiceProgressFill.Width = (int)((_practiceProgressBar.Width * percentage) / 100.0);
                
                // Hiệu ứng màu sắc theo tiến độ câu hỏi
                if (percentage >= 100) _practiceProgressFill.BackColor = Color.FromArgb(46, 204, 113); // Green
                else if (percentage > 50) _practiceProgressFill.BackColor = Color.FromArgb(52, 152, 219); // Blue
                else _practiceProgressFill.BackColor = Color.FromArgb(250, 173, 20); // Yellow
            }
            else
            {
                _lblPracticeProgress.Text = $"⚡  Tiến độ: 0% (0/0)";
                _practiceProgressFill.Width = 0;
            }
        }

        private void HandleStudyPractice(bool silent = false)
        {
            if (_practiceItem == null) return;
            
            _manualStudyItem = _practiceItem;
            
            // Cuộn xuống phần ôn luyện (Không chạy bộ đếm cho Ôn luyện nữa)
            if (_cardWrapper != null)
                _cardWrapper.AutoScrollPosition = new Point(0, _unifiedCard.Height);

            if (!silent)
                MessageBox.Show($"Đã ưu tiên hiển thị phần ôn luyện cho khóa học:\n{_course.Name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenPracticeForm() { } 

        private async Task HandleAutoPracticeAsync()
        {
            if (_practiceIid == 0 || !_practiceExamScoIid.HasValue || !_practiceBankIid.HasValue) 
            {
                MessageBox.Show("Thông tin ôn luyện chưa được tải đầy đủ. Vui lòng thử lại sau giây lát.", "Thông báo");
                return;
            }
 
            var confirm = MessageBox.Show("Hệ thống sẽ tự động quét toàn bộ ngân hàng câu hỏi, lấy đáp án đúng (is_answer: 1) và nộp bài hàng loạt. Tiếp tục?", "Xác nhận Auto", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
 
            try
            {
                this.Cursor = Cursors.WaitCursor;
                AppLog.Info($"[Auto Ôn luyện] Bắt đầu quét ngân hàng câu hỏi cho khóa học [{_course.Name}]...");

                // Lấy lại ri/rit tươi nhất nếu có thể (lấy từ thông tin chi tiết đã lưu hoặc fetch lại)
                var practiceDetail = await _courseService.GetPracticeDetailAsync(_practiceIid, _course.Iid);
                string ri = practiceDetail?.SelectToken("result._expand.exam_sco.ri")?.ToString();
                string rit = practiceDetail?.SelectToken("result._expand.exam_sco.rit")?.ToString();

                var response = await _courseService.GetQuestionBankStatusAsync(_practiceExamScoIid.Value, _practiceBankIid.Value, _course.Iid);
                if (response != null && response["result"]?["questions"]?["result"] is JArray qArray)
                {
                    var questions = qArray.Children().ToList();
                    if (questions.Count == 0)
                    {
                        MessageBox.Show("Ngân hàng câu hỏi trống hoặc đã được giải hết.", "Thông báo");
                        return;
                    }

                    // Log đáp án đúng trước khi nộp
                    int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                    foreach (var q in questions)
                    {
                        string qContent = q["content"]?.ToString() ?? "Không rõ nội dung";
                        string qIidStr = q["iid"]?.ToString() ?? "0";
                        
                        // Tìm câu trả lời đúng (is_answer: 1) trong mc_answers hoặc answers2
                        var options = (q["mc_answers"] as JArray) ?? (q["answers2"] as JArray);
                        string rightAnsText = "Không tìm thấy is_answer:1";
                        if (options != null)
                        {
                            var correct = options.FirstOrDefault(a => a["is_answer"]?.ToString() == "1");
                            if (correct != null) rightAnsText = correct["text"]?.ToString() ?? "Unknown";
                        }
                        
                        AppLog.Info($"[Giải câu hỏi] ID {qIidStr}: {qContent} => Đáp án chuẩn: {rightAnsText}", 0, userIid);
                    }

                    AppLog.Info($"[Auto Ôn luyện] Đã thu thập {questions.Count} câu hỏi. Đang gọi API save-take nộp hàng loạt...", 0, userIid);

                    var saveRes = await _courseService.SavePracticeTakeAsync(_practiceIid, _practiceExamScoIid.Value, _course.Iid, questions, ri, rit);
                    
                    if (saveRes != null && (bool)saveRes["success"])
                    {
                        MessageBox.Show($"Thành công: Đã giải và nộp {questions.Count} câu hỏi ôn luyện!", "Thông báo");
                        await LoadPracticeStatsAsync(); // Refresh with new stats
                    }
                    else
                    {
                        string errMsg = saveRes?["message"]?.ToString() ?? "Lỗi không xác định từ máy chủ.";
                        MessageBox.Show($"Cảnh báo: Phản hồi từ server có thể không thành công. Lỗi: {errMsg}", "Thông báo");
                    }
                }
                else
                {
                    MessageBox.Show("Không thể lấy dữ liệu ngân hàng câu hỏi.", "Lỗi");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi trong quá trình Auto: {ex.Message}", "Lỗi");
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        private void SaveCountdownBar_OnPaint(object? sender, PaintEventArgs e)
        {
            if (_saveCountdownBar == null) return;

            var g = e.Graphics;
            var rect = _saveCountdownBar.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            g.Clear(_saveCountdownBar.BackColor);

            string text;
            double fillRatio;

            if (_autoStudyStoppedPermanently)
            {
                fillRatio = 1;
                text = "Hoàn thành";
            }
            else if (_saveCycleRunning)
            {
                fillRatio = 1;
                text = "Đang lưu...";
            }
            else
            {
                int v = Math.Clamp(_secondsUntilNextSave, 0, _currentIntervalSeconds);
                fillRatio = _currentIntervalSeconds <= 0 ? 0 : (double)v / _currentIntervalSeconds;
                text = v <= 0 ? "0s" : $"{v}s";
            }

            int fillW = (int)Math.Round(rect.Width * fillRatio);
            if (fillW > 0)
            {
                var fillRect = new Rectangle(rect.X, rect.Y, fillW, rect.Height);
                using var br = new SolidBrush(ColorPalette.Primary);
                g.FillRectangle(br, fillRect);
            }

            using var font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            Color textColor = fillRatio > 0.5 ? Color.White : ColorPalette.TextPrimary;
            TextRenderer.DrawText(g, text, font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void UpdateNextSaveStripUi()
        {
            _saveCountdownBar?.Invalidate();
        }
        
        private void StartAutoStudy()
        {
            _currentIntervalSeconds = ComputeNextIntervalSeconds();
            _secondsUntilNextSave = _currentIntervalSeconds;
            _autoStudyStoppedPermanently = false;
            UpdateNextSaveStripUi();

            _countdownTickTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTickTimer.Tick += OnCountdownTick;
            _countdownTickTimer.Start();
        }

        public void StopAutoStudy()
        {
            _autoStudyStoppedPermanently = true;
            if (_countdownTickTimer != null)
            {
                _countdownTickTimer.Stop();
                _countdownTickTimer.Enabled = false;
            }
        }

        private int ComputeNextIntervalSeconds()
        {
            if (_rawData?.result?.children == null) return SaveIntervalSeconds;

            dynamic nextItem = null;

            if (_manualStudyItem != null)
            {
                int manualRem = 0;
                if (TryGetRemainingTime(_manualStudyItem, out manualRem) && manualRem > 0)
                    nextItem = _manualStudyItem;
            }

            if (nextItem == null)
                nextItem = FindNextUncompletedItem(_rawData.result.children);

            if (nextItem != null)
            {
                int remaining = 0;
                if (TryGetRemainingTime(nextItem, out remaining) && remaining > 0)
                    return Math.Min(SaveIntervalSeconds, remaining);
            }

            return SaveIntervalSeconds;
        }

        private void OnCountdownTick(object? sender, EventArgs e)
        {
            if (_rawData?.result?.children == null || _autoStudyStoppedPermanently) return;
            if (_saveCycleRunning) return;

            if (_secondsUntilNextSave > 0)
            {
                _secondsUntilNextSave--;
                UpdateNextSaveStripUi();
            }

            if (_secondsUntilNextSave == 0)
            {
                _saveCycleRunning = true;
                UpdateNextSaveStripUi();
                RunAutoStudyCycleAsync();
            }
        }

        private async void RunAutoStudyCycleAsync()
        {
            bool stopForever = false;
            try
            {
                if (_rawData?.result?.children == null) return;

                dynamic nextItem = null;

                if (_manualStudyItem != null)
                {
                    if (TryGetRemainingTime(_manualStudyItem, out int manualRemaining) && manualRemaining > 0)
                    {
                        nextItem = _manualStudyItem;
                    }
                    else
                    {
                        _manualStudyItem = FindNextUncompletedItemAfter(_manualStudyItem);
                        nextItem = _manualStudyItem;
                    }
                }

                if (nextItem == null)
                {
                    nextItem = FindNextUncompletedItem(_rawData.result.children);
                }

                int firstRemaining = 0;
                bool hasFirstRemaining = nextItem != null && TryGetRemainingTime(nextItem, out firstRemaining);

                if (nextItem != null && !hasFirstRemaining)
                {
                    _sessionList.RefreshTrackTimeData();
                    return;
                }

                if (nextItem != null && firstRemaining <= 0)
                {
                    if (_manualStudyItem != null && object.ReferenceEquals(nextItem, _manualStudyItem))
                    {
                        _manualStudyItem = null;
                    }

                    nextItem = FindNextUncompletedItem(_rawData.result.children);
                }

                if (nextItem == null)
                {
                    _countdownTickTimer?.Stop();
                    stopForever = true;
                    _autoStudyStoppedPermanently = true;
                    MessageBox.Show("Khóa học đã hoàn thành toàn bộ thời lượng yêu cầu!", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Luôn highlight bài đang học để user biết tiến độ
                _sessionList.HighlightItem(nextItem);

                if (!TryGetItemIid(nextItem, out int itemIid))
                {
                    _sessionList.RefreshTrackTimeData();
                    return;
                }
                string itemNtype = nextItem.ntype.ToString();
                int timeToSave = 15;

                if (TryGetRemainingTime(nextItem, out int remaining))
                {
                    if (remaining <= 0)
                    {
                        _sessionList.RefreshTrackTimeData();
                        return;
                    }
                    timeToSave = Math.Min(15, remaining);
                }

                if (this.IsDisposed) return;

                bool success = false;
                success = await _courseService.SaveItemLearningTimeAsync(itemIid, itemNtype, _course.Iid, timeToSave);
                
                if (this.IsDisposed) return;

                if (success)
                {
                    int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                    if (itemNtype != "practice")
                    {
                        AppLog.Info($"Thành công: Đã lưu {timeToSave}s cho bài [{nextItem.name}]", (int)nextItem.iid, userIid);
                    }
                    
                    bool syncedFromServer = false;
                    try
                    {
                        var fresh = await _courseService.GetDetailLearningTimeAsync(_course.Iid);
                        if (fresh != null && !this.IsDisposed)
                        {
                            MergeActualLearningTimesFromServer(fresh);
                            syncedFromServer = true;
                        }
                    }
                    catch
                    {
                        // ignored — fallback bên dưới
                    }

                    if (!syncedFromServer)
                    {
                        try { nextItem.actual_learning_time += timeToSave; } catch { }
                    }

                    _sessionList.EnableLogButton(itemIid);
                    _sessionList.RefreshTrackTimeData();

                    if (_manualStudyItem != null && object.ReferenceEquals(nextItem, _manualStudyItem))
                    {
                        if (TryGetRemainingTime(_manualStudyItem, out int manualRemaining) && manualRemaining <= 0)
                        {
                            _manualStudyItem = FindNextUncompletedItemAfter(_manualStudyItem);
                        }
                    }
                }

                _saveCallCount++;

                if (_saveCallCount == 3)
                {
                    // Check KPI trước khi gọi track time hoặc định kỳ
                    KpiData kpiData = await _courseService.CheckTodayKpiTimeAsync(_course.Iid);
                    UpdateKpiDisplay(kpiData);

                    if (kpiData.IsReached && !this.IsDisposed)
                    {
                        StopAutoStudy();
                        MessageBox.Show("Đã đạt KPI học tập trong ngày! Tự động dừng để đảm bảo an toàn.", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                        AppLog.Info($"Dừng tự động học: Đã đạt KPI hàng ngày cho khóa học [{_course.Name}]", 0, userIid);
                        return;
                    }

                    await _courseService.TrackTimeAsync(_course.Iid, 15.0);
                    int userIidTrack = _courseService.AuthService?.CurrentUserIid ?? 0;
                    AppLog.Info($"Đã gọi Track-Time (15s) cho khóa học [{_course.Name}]", 0, userIidTrack);
                    _saveCallCount = 0;
                }
            }
            finally
            {
                _saveCycleRunning = false;
                if (!stopForever && _countdownTickTimer != null && _countdownTickTimer.Enabled)
                {
                    _currentIntervalSeconds = ComputeNextIntervalSeconds();
                    _secondsUntilNextSave = _currentIntervalSeconds;
                }
                UpdateNextSaveStripUi();
            }
        }

        private bool TryGetRemainingTime(dynamic item, out int remaining)
        {
            remaining = 0;
            try
            {
                if (!TryGetIntValue(item.actual_learning_time, out int actual)) return false;
                if (!TryGetIntValue(item.min_learning_time, out int min)) return false;
                if (min <= 0) return false;
                remaining = min - actual;
                return true;
            }
            catch
            {
                return false;
            }
        }
        private dynamic FindNextUncompletedItem(dynamic children)
        {
            if (children == null) return null;

            foreach (var item in children)
            {
                try
                {
                    if (item.children != null)
                    {
                        var deeper = FindNextUncompletedItem(item.children);
                        if (deeper != null) return deeper;
                    }
                }
                catch { }

                try
                {
                    if (!TryGetIntValue(item.min_learning_time, out int min) || min <= 0) continue;
                    if (!TryGetIntValue(item.actual_learning_time, out int actual)) continue;
                    if (actual < min)
                        return item;
                }
                catch { }
            }

            return null;
        }

        private dynamic FindNextUncompletedItemAfter(dynamic currentItem)
        {
            if (_rawData?.result?.children == null || currentItem == null) return null;

            var orderedItems = new List<dynamic>();
            CollectTrackableItems(_rawData.result.children, orderedItems);
            if (orderedItems.Count == 0) return null;

            string currentIidKey = GetItemIidKey(currentItem);
            if (string.IsNullOrWhiteSpace(currentIidKey))
            {
                return FindNextUncompletedItem(_rawData.result.children);
            }

            int currentIndex = -1;
            for (int i = 0; i < orderedItems.Count; i++)
            {
                if (string.Equals(GetItemIidKey(orderedItems[i]), currentIidKey, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return FindNextUncompletedItem(_rawData.result.children);
            }

            for (int i = currentIndex + 1; i < orderedItems.Count; i++)
            {
                if (TryGetRemainingTime(orderedItems[i], out int remaining) && remaining > 0)
                {
                    return orderedItems[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Thứ tự phẳng khớp ưu tiên học: con trước cha (cùng logic <see cref="FindNextUncompletedItem"/>).
        /// </summary>
        private void CollectTrackableItems(dynamic children, List<dynamic> output)
        {
            if (children == null || output == null) return;

            foreach (var item in children)
            {
                try
                {
                    if (item.children != null)
                        CollectTrackableItems(item.children, output);
                }
                catch { }

                int ignoredRemaining;
                if (TryGetRemainingTime(item, out ignoredRemaining))
                    output.Add(item);
            }
        }

        private string GetItemIidKey(dynamic item)
        {
            try
            {
                return item?.iid?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool TryGetItemIid(dynamic item, out int itemIid)
        {
            itemIid = 0;
            try
            {
                return TryGetIntValue(item.iid, out itemIid);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetIntValue(dynamic value, out int result)
        {
            result = 0;
            if (value == null) return false;

            try
            {
                if (value is JValue jv && jv.Value != null)
                {
                    return int.TryParse(jv.Value.ToString(), out result);
                }

                return int.TryParse(value.ToString(), out result);
            }
            catch
            {
                return false;
            }
        }

        private void MergeActualLearningTimesFromServer(SubjectDetails fresh)
        {
            if (fresh == null || _rawData?.result == null) return;

            var jo = JObject.FromObject(fresh);
            var map = new Dictionary<int, int>();
            CollectActualLearningTimesFromJson(jo["result"], map);
            if (map.Count == 0) return;
            ApplyServerActualTimesToTree(_rawData.result, map);
        }

        private static void CollectActualLearningTimesFromJson(JToken? token, Dictionary<int, int> map)
        {
            if (token is not JObject o) return;

            JToken? iidTok = o["iid"];
            JToken? actualTok = o["actual_learning_time"];
            if (iidTok != null && iidTok.Type != JTokenType.Null
                && actualTok != null && actualTok.Type != JTokenType.Null
                && int.TryParse(iidTok.ToString(), out int iid)
                && int.TryParse(actualTok.ToString(), out int secs))
            {
                map[iid] = secs;
            }

            if (o["children"] is JArray arr)
            {
                foreach (JToken? c in arr)
                    CollectActualLearningTimesFromJson(c, map);
            }
        }

        private void ApplyServerActualTimesToTree(dynamic? node, Dictionary<int, int> map)
        {
            if (node == null) return;

            try
            {
                if (TryGetIntValue(node.iid, out int iid))
                {
                    if (map.TryGetValue(iid, out int serverSecs))
                        node.actual_learning_time = serverSecs;
                }
            }
            catch
            {
                // ignore node
            }

            try
            {
                var children = node.children;
                if (children == null) return;
                foreach (var c in children)
                    ApplyServerActualTimesToTree(c, map);
            }
            catch
            {
                // ignore children
            }
        }

        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
            }
        }
    }
}