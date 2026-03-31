namespace LotusLMSClient.UserControls
{
    partial class CourseListControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel panelFilters;
        private System.Windows.Forms.Button btnAll;
        private System.Windows.Forms.Button btnLearning;
        private System.Windows.Forms.Button btnCompleted;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutCourses;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.panelFilters = new System.Windows.Forms.Panel();
            this.tableLayoutPanelFilters = new System.Windows.Forms.TableLayoutPanel();
            this.flowLayoutPanelButtons = new System.Windows.Forms.FlowLayoutPanel();
            this.btnAll = new System.Windows.Forms.Button();
            this.btnLearning = new System.Windows.Forms.Button();
            this.btnCompleted = new System.Windows.Forms.Button();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.flowLayoutCourses = new System.Windows.Forms.FlowLayoutPanel();
            this.panelFilters.SuspendLayout();
            this.tableLayoutPanelFilters.SuspendLayout();
            this.flowLayoutPanelButtons.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 20F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(25, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(235, 37);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "📚 Khóa học của tôi";
            // 
            // panelFilters
            // 
            this.panelFilters.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelFilters.Controls.Add(this.tableLayoutPanelFilters);
            this.panelFilters.Location = new System.Drawing.Point(26, 75);
            this.panelFilters.Name = "panelFilters";
            this.panelFilters.Size = new System.Drawing.Size(750, 45);
            this.panelFilters.TabIndex = 1;
            // 
            // tableLayoutPanelFilters
            // 
            this.tableLayoutPanelFilters.ColumnCount = 2;
            this.tableLayoutPanelFilters.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tableLayoutPanelFilters.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelFilters.Controls.Add(this.flowLayoutPanelButtons, 0, 0);
            this.tableLayoutPanelFilters.Controls.Add(this.txtSearch, 1, 0);
            this.tableLayoutPanelFilters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanelFilters.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelFilters.Name = "tableLayoutPanelFilters";
            this.tableLayoutPanelFilters.RowCount = 1;
            this.tableLayoutPanelFilters.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelFilters.Size = new System.Drawing.Size(750, 45);
            this.tableLayoutPanelFilters.TabIndex = 0;
            // 
            // flowLayoutPanelButtons
            // 
            this.flowLayoutPanelButtons.AutoSize = true;
            this.flowLayoutPanelButtons.Controls.Add(this.btnAll);
            this.flowLayoutPanelButtons.Controls.Add(this.btnLearning);
            this.flowLayoutPanelButtons.Controls.Add(this.btnCompleted);
            this.flowLayoutPanelButtons.Location = new System.Drawing.Point(3, 3);
            this.flowLayoutPanelButtons.Name = "flowLayoutPanelButtons";
            this.flowLayoutPanelButtons.Size = new System.Drawing.Size(356, 36);
            this.flowLayoutPanelButtons.TabIndex = 0;
            this.flowLayoutPanelButtons.WrapContents = false;
            // 
            // btnAll
            // 
            this.btnAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAll.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnAll.Location = new System.Drawing.Point(3, 3);
            this.btnAll.Name = "btnAll";
            this.btnAll.Size = new System.Drawing.Size(100, 30);
            this.btnAll.TabIndex = 0;
            this.btnAll.Text = "Tất cả";
            this.btnAll.UseVisualStyleBackColor = true;
            // 
            // btnLearning
            // 
            this.btnLearning.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLearning.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnLearning.Location = new System.Drawing.Point(109, 3);
            this.btnLearning.Name = "btnLearning";
            this.btnLearning.Size = new System.Drawing.Size(120, 30);
            this.btnLearning.TabIndex = 1;
            this.btnLearning.Text = "Đang học";
            this.btnLearning.UseVisualStyleBackColor = true;
            // 
            // btnCompleted
            // 
            this.btnCompleted.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCompleted.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnCompleted.Location = new System.Drawing.Point(235, 3);
            this.btnCompleted.Name = "btnCompleted";
            this.btnCompleted.Size = new System.Drawing.Size(118, 30);
            this.btnCompleted.TabIndex = 2;
            this.btnCompleted.Text = "Đã hoàn thành";
            this.btnCompleted.UseVisualStyleBackColor = true;
            // 
            // txtSearch
            // 
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.txtSearch.Location = new System.Drawing.Point(365, 6);
            this.txtSearch.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(382, 29);
            this.txtSearch.TabIndex = 1;
            this.txtSearch.Text = "🔍 Tìm kiếm khóa học...";
            // 
            // flowLayoutCourses
            // 
            this.flowLayoutCourses.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutCourses.AutoScroll = true;
            this.flowLayoutCourses.Location = new System.Drawing.Point(26, 135);
            this.flowLayoutCourses.Name = "flowLayoutCourses";
            this.flowLayoutCourses.Size = new System.Drawing.Size(750, 545);
            this.flowLayoutCourses.TabIndex = 2;
            // 
            // CourseListControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.flowLayoutCourses);
            this.Controls.Add(this.panelFilters);
            this.Controls.Add(this.lblTitle);
            this.Name = "CourseListControl";
            this.Size = new System.Drawing.Size(800, 700);
            this.panelFilters.ResumeLayout(false);
            this.tableLayoutPanelFilters.ResumeLayout(false);
            this.tableLayoutPanelFilters.PerformLayout();
            this.flowLayoutPanelButtons.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelFilters;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelButtons;
    }
}
