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
        private SessionListControl _sessionList;
        private readonly CourseService _courseService;

        private System.Windows.Forms.Timer _autoStudyTimer;
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

            _sessionList = new SessionListControl();
            _sessionList.Dock = DockStyle.Fill;
            _sessionList.OnLessonSelected = (item) => 
            {
                _manualStudyItem = item;
                MessageBox.Show($"Đã ưu tiên tự động học bài:\n{item.name}", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _contentPanel.Controls.Add(_sessionList);

            if (_rawData != null)
            {
                _sessionList.DisplayTrackTimeData(_rawData);
                StartAutoStudy();
            }
        }
        
        private void StartAutoStudy()
        {
            _autoStudyTimer = new System.Windows.Forms.Timer();
            _autoStudyTimer.Interval = 15000; // 15 seconds real-time
            _autoStudyTimer.Tick += OnAutoStudyTimerTick;
            _autoStudyTimer.Start();
        }

        private async void OnAutoStudyTimerTick(object sender, EventArgs e)
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
                    // Item da hoan thanh -> chuyen sang item ke tiep theo thu tu.
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
                _autoStudyTimer.Stop();
                MessageBox.Show("Khóa học đã hoàn thành toàn bộ thời lượng yêu cầu!", "Lotus LMS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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

            // Gọi API luu-tien-do
            bool success = await _courseService.SaveItemLearningTimeAsync(itemIid, itemNtype, _course.Iid, timeToSave);
            if (success)
            {
                try { nextItem.actual_learning_time += timeToSave; } catch { }
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
                _saveCallCount = 0;
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
                    int actual = (int)item.actual_learning_time;
                    int min = (int)item.min_learning_time;
                    if (min > 0 && actual < min)
                        return item;
                }
                catch { }

                try 
                {
                    if (item.children != null)
                    {
                        var result = FindNextUncompletedItem(item.children);
                        if (result != null) return result;
                    }
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

        private void CollectTrackableItems(dynamic children, List<dynamic> output)
        {
            if (children == null || output == null) return;

            foreach (var item in children)
            {
                int ignoredRemaining;
                if (TryGetRemainingTime(item, out ignoredRemaining))
                {
                    output.Add(item);
                }

                try
                {
                    if (item.children != null)
                    {
                        CollectTrackableItems(item.children, output);
                    }
                }
                catch { }
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

    }
}