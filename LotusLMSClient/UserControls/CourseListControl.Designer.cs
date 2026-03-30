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
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.btnAll = new System.Windows.Forms.Button();
            this.btnLearning = new System.Windows.Forms.Button();
            this.btnCompleted = new System.Windows.Forms.Button();
            this.flowLayoutCourses = new System.Windows.Forms.FlowLayoutPanel();
            this.panelFilters.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(20, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(206, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "📚 Khóa học của tôi";
            // 
            // panelFilters
            // 
            this.panelFilters.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelFilters.Controls.Add(this.btnCompleted);
            this.panelFilters.Controls.Add(this.btnLearning);
            this.panelFilters.Controls.Add(this.btnAll);
            this.panelFilters.Controls.Add(this.txtSearch);
            this.panelFilters.Location = new System.Drawing.Point(26, 70);
            this.panelFilters.Name = "panelFilters";
            this.panelFilters.Size = new System.Drawing.Size(750, 40);
            this.panelFilters.TabIndex = 1;
            // 
            // txtSearch
            // 
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.txtSearch.Location = new System.Drawing.Point(500, 5);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(240, 29);
            this.txtSearch.TabIndex = 0;
            this.txtSearch.Text = "🔍 Tìm kiếm khóa học...";
            // 
            // btnAll
            // 
            this.btnAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAll.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnAll.Location = new System.Drawing.Point(0, 5);
            this.btnAll.Name = "btnAll";
            this.btnAll.Size = new System.Drawing.Size(100, 30);
            this.btnAll.TabIndex = 1;
            this.btnAll.Text = "Tất cả";
            this.btnAll.UseVisualStyleBackColor = true;
            // 
            // btnLearning
            // 
            this.btnLearning.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLearning.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnLearning.Location = new System.Drawing.Point(110, 5);
            this.btnLearning.Name = "btnLearning";
            this.btnLearning.Size = new System.Drawing.Size(120, 30);
            this.btnLearning.TabIndex = 2;
            this.btnLearning.Text = "Đang học";
            this.btnLearning.UseVisualStyleBackColor = true;
            // 
            // btnCompleted
            // 
            this.btnCompleted.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCompleted.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnCompleted.Location = new System.Drawing.Point(240, 5);
            this.btnCompleted.Name = "btnCompleted";
            this.btnCompleted.Size = new System.Drawing.Size(120, 30);
            this.btnCompleted.TabIndex = 3;
            this.btnCompleted.Text = "Đã hoàn thành";
            this.btnCompleted.UseVisualStyleBackColor = true;
            // 
            // flowLayoutCourses
            // 
            this.flowLayoutCourses.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutCourses.AutoScroll = true;
            this.flowLayoutCourses.Location = new System.Drawing.Point(26, 130);
            this.flowLayoutCourses.Name = "flowLayoutCourses";
            this.flowLayoutCourses.Size = new System.Drawing.Size(750, 550);
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
            this.panelFilters.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}
