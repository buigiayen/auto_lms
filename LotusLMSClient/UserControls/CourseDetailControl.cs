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

        // -- UI Controls --
        private Panel _headerPanel;
        private Button _btnBack;
        private Label _lblCourseName;
        private Panel _contentPanel;
        private TreeView _treeView;
        private Label _lblStatus;

        /// <summary>
        /// Sự kiện được kích hoạt khi người dùng nhấn "Quay lại".
        /// </summary>
        public Action OnBack { get; set; }

        public CourseDetailControl(Course course, SubjectDetails rawData)
        {
            InitializeComponent();
            _course = course;
            _rawData = rawData;

            BuildUI();
            PopulateTree();
        }

        // ─────────────────────────────────────────────────────────
        //  Build UI
        // ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            this.BackColor = ColorPalette.ContentBg;
            this.Dock = DockStyle.Fill;

            BuildHeader();
            BuildContent();

            this.Controls.Add(_contentPanel);
            this.Controls.Add(_headerPanel);   // header trên cùng
        }

        private void BuildHeader()
        {
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = ColorPalette.Primary,
                Padding = new Padding(12, 0, 12, 0)
            };

            _btnBack = new Button
            {
                Text = "← Quay lại",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Width = 110,
                Height = 36,
                Location = new Point(12, 14),
                Cursor = Cursors.Hand
            };
            _btnBack.FlatAppearance.BorderSize = 1;
            _btnBack.FlatAppearance.BorderColor = Color.White;
            _btnBack.FlatAppearance.MouseOverBackColor = ColorPalette.PrimaryDark;
            _btnBack.Click += (s, e) => OnBack?.Invoke();

            _lblCourseName = new Label
            {
                Text = $"📚  {_course.Name}",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(140, 0),
                Size = new Size(1000, 64)
            };

            _headerPanel.Controls.Add(_btnBack);
            _headerPanel.Controls.Add(_lblCourseName);
        }

        private void BuildContent()
        {
            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                BackColor = ColorPalette.ContentBg
            };

            // Status / loading label
            _lblStatus = new Label
            {
                Text = "⏳ Đang phân tích dữ liệu bài học...",
                Font = new Font("Segoe UI", 11F),
                ForeColor = ColorPalette.TextSecondary,
                AutoSize = true,
                Location = new Point(20, 20)
            };

            // TreeView
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11F),
                BackColor = Color.White,
                ForeColor = ColorPalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                ItemHeight = 30,
                Indent = 26,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Scrollable = true,
                Visible = false,
                Margin = new Padding(5)
            };

            // Card wrapper cho TreeView
            var treeCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(8),
                Visible = false
            };
            treeCard.Controls.Add(_treeView);

            // Lưu reference để hiện sau khi populate
            _treeCard = treeCard;

            _contentPanel.Controls.Add(treeCard);
            _contentPanel.Controls.Add(_lblStatus);
        }

        // ref tới card để toggle visibility
        private Panel _treeCard;

        // ─────────────────────────────────────────────────────────
        //  Populate TreeView
        // ─────────────────────────────────────────────────────────

        private void PopulateTree()
        {
            if (_rawData == null)
            {
                ShowStatus("❌ Không có dữ liệu trả về từ API.", ColorPalette.Error);
                return;
            }

            // Kiểm tra success
            var successToken = _rawData.success;
            //if (successToken != null && successToken.Type == JTokenType.Boolean && !(bool)successToken)
            //{
            //    var errCode = _rawData.ToString() ?? "?";
            //    ShowStatus($"❌ API trả về lỗi (err_code: {errCode}). Vui lòng kiểm tra session.", ColorPalette.Error);
            //    return;
            //}

            //var nodes = CourseService.ParseLearningHierarchy(_rawData);
            //if (nodes.Count == 0)
            //{
            //    ShowStatus("ℹ️ Không tìm thấy tên bài học trong dữ liệu API.", ColorPalette.Warning);
            //    return;
            //}

            // Populate TreeView
            _treeView.BeginUpdate();
            _treeView.Nodes.Clear();

            var rootNode = new TreeNode($"📚  {_course.Name}")
            {
                NodeFont = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = ColorPalette.Primary
            };

            //foreach (var node in nodes)
            //{
            //    rootNode.Nodes.Add(BuildTreeNode(node, depth: 0));
            //}

            _treeView.Nodes.Add(rootNode);
            _treeView.ExpandAll();
            _treeView.EndUpdate();

            // Cuộn lên đầu
            if (_treeView.Nodes.Count > 0)
                _treeView.Nodes[0].EnsureVisible();

            // Ẩn status, hiện TreeView card
            _lblStatus.Visible = false;
            _treeCard.Visible = true;
            _treeView.Visible = true;
        }

        private TreeNode BuildTreeNode(LearningTreeNode node, int depth)
        {
            string prefix = depth == 0 ? "📂  " : "🎬  ";
            var tn = new TreeNode($"{prefix}{node.Name}");

            if (depth == 0)
                tn.NodeFont = new Font("Segoe UI", 10F, FontStyle.Bold);

            foreach (var child in node.Children)
            {
                tn.Nodes.Add(BuildTreeNode(child, depth + 1));
            }

            return tn;
        }

        private void ShowStatus(string message, Color color)
        {
            _lblStatus.Text = message;
            _lblStatus.ForeColor = color;
            _lblStatus.Font = new Font("Segoe UI", 11F);
            _lblStatus.Visible = true;
            _treeCard.Visible = false;
        }
    }
}
