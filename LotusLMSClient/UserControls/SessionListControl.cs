using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;
using LotusLMSClient.Forms;

namespace LotusLMSClient.UserControls
{
    public partial class SessionListControl : UserControl
    {
        private const int TimeColumnIndex = 1;
        private const int LogColumnIndex = 2;
        private static readonly Color TimeIncompleteColor = Color.FromArgb(200, 45, 45);
        private static readonly Color TimeCompleteColor = Color.FromArgb(13, 110, 253);
        private static readonly Color TimeMinPartColor = Color.Black;

        private sealed class TimePaintInfo
        {
            public string ActualText = string.Empty;
            public string MinText = string.Empty;
            public bool Complete;
        }

        public Action<dynamic> OnLessonSelected;
        public int CurrentUserIid { get; set; }

        public SessionListControl()
        {
            InitializeComponent();
            SetupUI();
            PopulateDummyData();
        }

        private void SetupUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            lblTitle.ForeColor = ColorPalette.TextPrimary;
            lblSubtitle.ForeColor = ColorPalette.TextSecondary;
            
            // Premium DataGridView Styling
            dgvSessions.BackgroundColor = ColorPalette.CardBg;
            dgvSessions.GridColor = Color.FromArgb(230, 230, 230);
            dgvSessions.BorderStyle = BorderStyle.None;
            dgvSessions.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvSessions.EnableHeadersVisualStyles = false;
            
            // Header styling
            dgvSessions.ColumnHeadersDefaultCellStyle.BackColor = ColorPalette.PrimaryDark;
            dgvSessions.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvSessions.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            dgvSessions.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvSessions.ColumnHeadersHeight = 45;
            
            // Rows styling
            dgvSessions.DefaultCellStyle.BackColor = Color.White;
            dgvSessions.DefaultCellStyle.ForeColor = ColorPalette.TextPrimary;
            dgvSessions.DefaultCellStyle.Font = new Font("Segoe UI", 10F);
            dgvSessions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 245, 255);
            dgvSessions.DefaultCellStyle.SelectionForeColor = ColorPalette.Primary;
            
            // Alternating rows for readability
            dgvSessions.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
            
            dgvSessions.RowTemplate.Height = 45;
            dgvSessions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            // Column Adjustments for full visibility
            ColDate.HeaderText = "Tên bài học";
            ColDate.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            ColDate.MinimumWidth = 200;

            ColSubject.HeaderText = "Thời gian";
            ColSubject.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            ColSubject.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            ColSubject.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // Let the name column fill remaining space
            dgvSessions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            ColDate.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; 
            ColDate.FillWeight = 70; // Give name column more weight
            
            ColSubject.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            
            ColLog.HeaderText = "Log";
            ColLog.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            ColLog.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvSessions.CellClick += DgvSessions_CellClick;
            dgvSessions.CellContentClick += DgvSessions_CellContentClick;
            dgvSessions.CellPainting += DgvSessions_CellPainting;
        }

        private void DgvSessions_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != TimeColumnIndex)
                return;

            if (e.RowIndex >= dgvSessions.Rows.Count)
                return;

            var row = dgvSessions.Rows[e.RowIndex];
            var cell = row.Cells[TimeColumnIndex];
            if (cell.Tag is not TimePaintInfo info)
            {
                return;
            }

            e.Handled = true;
            e.PaintBackground(e.CellBounds, true);

            Font font = e.CellStyle.Font ?? dgvSessions.Font;
            TextFormatFlags flags = TextFormatFlags.NoPadding
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine;

            string sep = " / ";
            Color actualColor = info.Complete ? TimeCompleteColor : TimeIncompleteColor;

            Size szActual = TextRenderer.MeasureText(e.Graphics, info.ActualText, font, Size.Empty, flags);
            Size szSep = TextRenderer.MeasureText(e.Graphics, sep, font, Size.Empty, flags);
            Size szMin = TextRenderer.MeasureText(e.Graphics, info.MinText, font, Size.Empty, flags);
            int totalWidth = szActual.Width + szSep.Width + szMin.Width;
            int startX = e.CellBounds.Left + (e.CellBounds.Width - totalWidth) / 2;
            int y = e.CellBounds.Top;

            var rectActual = new Rectangle(startX, y, szActual.Width, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, info.ActualText, font, rectActual, actualColor, flags);

            startX += szActual.Width;
            var rectSep = new Rectangle(startX, y, szSep.Width, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, sep, font, rectSep, TimeMinPartColor, flags);

            startX += szSep.Width;
            var rectMin = new Rectangle(startX, y, szMin.Width, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, info.MinText, font, rectMin, TimeMinPartColor, flags);

            e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        }

        private void DgvSessions_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != LogColumnIndex)
            {
                var tag = dgvSessions.Rows[e.RowIndex].Tag;
                if (tag != null)
                {
                    OnLessonSelected?.Invoke(tag);
                }
            }
        }

        private void DgvSessions_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != LogColumnIndex) return;

            var row = dgvSessions.Rows[e.RowIndex];
            if (row.Tag == null || row.Cells[e.ColumnIndex].Value == null || string.IsNullOrWhiteSpace(row.Cells[e.ColumnIndex].Value.ToString())) 
                return;

            dynamic item = row.Tag;
            try
            {
                int itemIid = Convert.ToInt32(item.iid);
                var logger = new LogViewerForm(itemIid, CurrentUserIid);
                logger.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở log: " + ex.Message);
            }
        }

        public void DisplayTrackTimeData(SubjectDetails details)
        {
            dgvSessions.Rows.Clear();
            if (details?.result?.children == null) return;

            AddItemsRecursive(details.result.children, 0);
        }

        private void AddItemsRecursive(dynamic children, int depth)
        {
            if (children == null) return;

            foreach (var item in children)
            {
                string indent = new string(' ', depth * 4);

                // Add Row: Name, TimeSpent, Log (initially hidden)
                int rowIndex = dgvSessions.Rows.Add(
                    indent + item.name,
                    string.Empty,
                    string.Empty
                );
                dgvSessions.Rows[rowIndex].Tag = item;
                ApplyTimeCellForItem(dgvSessions.Rows[rowIndex], item);

                // Recursively add children if any exist
                try
                {
                    if (item.children != null)
                    {
                        AddItemsRecursive(item.children, depth + 1);
                    }
                }
                catch { }
            }
        }

        private string FormatTime(int totalSeconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            return $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        public void RefreshTrackTimeData()
        {
            foreach (DataGridViewRow row in dgvSessions.Rows)
            {
                if (row.Tag != null)
                {
                    ApplyTimeCellForItem(row, row.Tag);
                }
            }
        }

        public void HighlightItem(dynamic item)
        {
            if (item == null) return;

            string targetIid = string.Empty;
            try { targetIid = item.iid.ToString(); } catch { return; }

            foreach (DataGridViewRow row in dgvSessions.Rows)
            {
                if (row.Tag == null) continue;

                string rowIid = string.Empty;
                try { rowIid = ((dynamic)row.Tag).iid.ToString(); } catch { continue; }

                if (string.Equals(rowIid, targetIid, StringComparison.Ordinal))
                {
                    row.Selected = true;
                    // Scroll to row if not visible
                    if (!row.Displayed)
                    {
                        dgvSessions.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2);
                    }
                    break;
                }
            }
        }

        public void EnableLogButton(int itemIid)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => EnableLogButton(itemIid)));
                return;
            }

            foreach (DataGridViewRow row in dgvSessions.Rows)
            {
                if (row.Tag == null) continue;
                try
                {
                    int rowIid = Convert.ToInt32(((dynamic)row.Tag).iid);
                    if (rowIid == itemIid)
                    {
                        if (row.Cells[LogColumnIndex].Value == null || string.IsNullOrWhiteSpace(row.Cells[LogColumnIndex].Value.ToString()))
                        {
                            row.Cells[LogColumnIndex].Value = "📄 Xem Log";
                        }
                        return;
                    }
                }
                catch { }
            }
        }

        private void ApplyTimeCellForItem(DataGridViewRow row, dynamic item)
        {
            var cell = row.Cells[TimeColumnIndex];
            if (!TryGetLearningTimes(item, out int actual, out int min) || min <= 0)
            {
                cell.Value = string.Empty;
                cell.Tag = null;
                return;
            }

            string actualStr = FormatTime(actual);
            string minStr = FormatTime(min);
            cell.Value = $"{actualStr} / {minStr}";
            cell.Tag = new TimePaintInfo
            {
                ActualText = actualStr,
                MinText = minStr,
                Complete = actual >= min
            };
        }

        private static bool TryGetLearningTimes(dynamic item, out int actual, out int min)
        {
            actual = 0;
            min = 0;
            try
            {
                actual = Convert.ToInt32(item.actual_learning_time);
                min = Convert.ToInt32(item.min_learning_time);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void PopulateDummyData()
        {
            int idx = dgvSessions.Rows.Add("Dữ liệu mẫu bài học", string.Empty, string.Empty);
            var row = dgvSessions.Rows[idx];
            row.Cells[TimeColumnIndex].Value = "00:00 / 01:00:00";
            row.Cells[TimeColumnIndex].Tag = new TimePaintInfo
            {
                ActualText = "00:00",
                MinText = "01:00:00",
                Complete = false
            };
        }
    }
}
