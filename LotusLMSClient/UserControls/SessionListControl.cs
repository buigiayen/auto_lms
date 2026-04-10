using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using LotusLMSClient.Helpers;
using LotusLMSClient.Models;

namespace LotusLMSClient.UserControls
{
    public partial class SessionListControl : UserControl
    {
        public Action<dynamic> OnLessonSelected;

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

            dgvSessions.CellClick += DgvSessions_CellClick;
        }

        private void DgvSessions_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var tag = dgvSessions.Rows[e.RowIndex].Tag;
                if (tag != null)
                {
                    OnLessonSelected?.Invoke(tag);
                }
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
                string timeStr = "";
                string statusStr = "";

                // Get metrics if they exist
                try
                {
                    int actual = (int)item.actual_learning_time;
                    int min = (int)item.min_learning_time;
                    
                    if (min > 0)
                    {
                        timeStr = $"{FormatTime(actual)} / {FormatTime(min)}";
                    }
                }
                catch { }

                // Add Row: Name, NType (hidden), Room (hidden), TimeSpent, Teacher (hidden)
                int rowIndex = dgvSessions.Rows.Add(
                    indent + item.name, 
                    timeStr
                );
                dgvSessions.Rows[rowIndex].Tag = item;

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
                    dynamic item = row.Tag;
                    try
                    {
                        int actual = (int)item.actual_learning_time;
                        int min = (int)item.min_learning_time;
                        if (min > 0)
                        {
                            row.Cells[1].Value = $"{FormatTime(actual)} / {FormatTime(min)}";
                        }
                    }
                    catch { }
                }
            }
        }

        private void PopulateDummyData()
        {
            dgvSessions.Rows.Add("Dữ liệu mẫu bài học", "00:00 / 01:00:00");
        }
    }
}
