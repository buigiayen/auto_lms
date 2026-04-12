using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Services;
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
        private Panel _contentPanel;
        private Label _lblStatus;
        private Panel _nextSaveStrip;
        private Panel _saveCountdownBar;
        private SessionListControl _sessionList;
        private readonly CourseService _courseService;

        private System.Windows.Forms.Timer _countdownTickTimer;
        private int _secondsUntilNextSave;
        private bool _saveCycleRunning;
        private bool _autoStudyStoppedPermanently;
        private const int SaveIntervalSeconds = 15;
        private int _currentIntervalSeconds = SaveIntervalSeconds; // Interval thực tế cho chu kỳ hiện tại
        private int _saveCallCount = 0;
        private dynamic _manualStudyItem = null;

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
                Height = 80, // Increased height for multiple lines
                BackColor = ColorPalette.Primary,
                Padding = new Padding(12, 10, 12, 10)
            };
            
            Panel btnContainer = new Panel
            {
                Dock = DockStyle.Left,
                Width = 95, // Smaller width
                BackColor = Color.Transparent,
                Padding = new Padding(0, 10, 0, 10) // Center button vertically
            };

            _btnBack = new Button
            {
                Text = "← Quay lại",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
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
                AutoEllipsis = false // Turn off ellipsis to allow wrapping
            };

            _headerPanel.Controls.Add(_lblCourseName);
            _headerPanel.Controls.Add(btnContainer);
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

            _sessionList = new SessionListControl();
            _sessionList.Dock = DockStyle.Fill;
            _sessionList.OnLessonSelected = (item) => 
            {
                _manualStudyItem = item;

                // Chỉ bắt đầu đếm giờ khi lần đầu chọn item
                if (_countdownTickTimer == null || !_countdownTickTimer.Enabled)
                {
                    _nextSaveStrip.Visible = true;
                    StartAutoStudy();
                    MessageBox.Show($"Bắt đầu tự động học bài:\n{item.name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Đã ưu tiên tự động học bài:\n{item.name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            _contentPanel.Controls.Add(_nextSaveStrip);
            _contentPanel.Controls.Add(_sessionList);

            if (_rawData != null)
            {
                _sessionList.CurrentUserIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                _sessionList.DisplayTrackTimeData(_rawData);
                // Không tự động bắt đầu — chờ user chọn item
                _nextSaveStrip.Visible = false;
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

        /// <summary>
        /// Tính interval thực tế: nếu item tiếp theo còn lại ít hơn SaveIntervalSeconds
        /// thì đếm ngược đúng bằng thời gian đó thay vì luôn là 15s.
        /// </summary>
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

                bool success = await _courseService.SaveItemLearningTimeAsync(itemIid, itemNtype, _course.Iid, timeToSave);
                
                if (this.IsDisposed) return;

                if (success)
                {
                    int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                    AppLog.Info($"Thành công: Đã lưu {timeToSave}s cho bài [{nextItem.name}]", (int)nextItem.iid, userIid);
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
                    await _courseService.TrackTimeAsync(_course.Iid, 15.0);
                    int userIid = _courseService.AuthService?.CurrentUserIid ?? 0;
                    AppLog.Info($"Đã gọi Track-Time (15s) cho khóa học [{_course.Name}]", 0, userIid);
                    _saveCallCount = 0;
                }
            }
            finally
            {
                _saveCycleRunning = false;
                if (!stopForever && _countdownTickTimer != null && _countdownTickTimer.Enabled)
                {
                    // Tính lại interval cho chu kỳ tiếp theo (item có thể đã hoàn thành, chuyển sang item mới)
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

        /// <summary>
        /// Tìm bài chưa đủ min: ưu tiên con (lá) trước cha.
        /// Tránh chọn nhóm 3.10 khi Phần 1/2 còn thiếu — gửi time vào cha thường không làm nhảy phần con trên UI / server.
        /// </summary>
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