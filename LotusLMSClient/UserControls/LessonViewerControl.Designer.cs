namespace LotusLMSClient.UserControls
{
    partial class LessonViewerControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Button btnBack;
        private System.Windows.Forms.Label lblCourseTitle;
        private System.Windows.Forms.Panel panelSidebar;
        private System.Windows.Forms.Label lblTocTitle;
        private System.Windows.Forms.ListBox lstLessons;
        private System.Windows.Forms.Panel panelContent;
        private Microsoft.Web.WebView2.WinForms.WebView2 webView;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;

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
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblCourseTitle = new System.Windows.Forms.Label();
            this.btnBack = new System.Windows.Forms.Button();
            this.panelSidebar = new System.Windows.Forms.Panel();
            this.lstLessons = new System.Windows.Forms.ListBox();
            this.lblTocTitle = new System.Windows.Forms.Label();
            this.panelContent = new System.Windows.Forms.Panel();
            this.webView = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.btnNext = new System.Windows.Forms.Button();
            this.btnPrev = new System.Windows.Forms.Button();
            this.panelTop.SuspendLayout();
            this.panelSidebar.SuspendLayout();
            this.panelContent.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.webView)).BeginInit();
            this.panelBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.BackColor = System.Drawing.Color.White;
            this.panelTop.Controls.Add(this.btnBack);
            this.panelTop.Controls.Add(this.lblCourseTitle);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(800, 50);
            this.panelTop.TabIndex = 0;
            // 
            // lblCourseTitle
            // 
            this.lblCourseTitle.AutoSize = true;
            this.lblCourseTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblCourseTitle.Location = new System.Drawing.Point(120, 12);
            this.lblCourseTitle.Name = "lblCourseTitle";
            this.lblCourseTitle.Size = new System.Drawing.Size(342, 25);
            this.lblCourseTitle.TabIndex = 1;
            this.lblCourseTitle.Text = "Lý thuyết GPLX B2 > Bài 3: Biển báo";
            // 
            // btnBack
            // 
            this.btnBack.FlatAppearance.BorderSize = 0;
            this.btnBack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBack.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.btnBack.Location = new System.Drawing.Point(10, 5);
            this.btnBack.Name = "btnBack";
            this.btnBack.Size = new System.Drawing.Size(100, 40);
            this.btnBack.TabIndex = 0;
            this.btnBack.Text = "← Quay lại";
            this.btnBack.UseVisualStyleBackColor = true;
            this.btnBack.Click += new System.EventHandler(this.btnBack_Click);
            // 
            // panelSidebar
            // 
            this.panelSidebar.Controls.Add(this.lstLessons);
            this.panelSidebar.Controls.Add(this.lblTocTitle);
            this.panelSidebar.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelSidebar.Location = new System.Drawing.Point(0, 50);
            this.panelSidebar.Name = "panelSidebar";
            this.panelSidebar.Size = new System.Drawing.Size(250, 650);
            this.panelSidebar.TabIndex = 1;
            // 
            // lstLessons
            // 
            this.lstLessons.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.lstLessons.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstLessons.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lstLessons.FormattingEnabled = true;
            this.lstLessons.ItemHeight = 20;
            this.lstLessons.Location = new System.Drawing.Point(0, 40);
            this.lstLessons.Name = "lstLessons";
            this.lstLessons.Size = new System.Drawing.Size(250, 610);
            this.lstLessons.TabIndex = 1;
            // 
            // lblTocTitle
            // 
            this.lblTocTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblTocTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblTocTitle.Location = new System.Drawing.Point(0, 0);
            this.lblTocTitle.Name = "lblTocTitle";
            this.lblTocTitle.Padding = new System.Windows.Forms.Padding(10, 10, 0, 0);
            this.lblTocTitle.Size = new System.Drawing.Size(250, 40);
            this.lblTocTitle.TabIndex = 0;
            this.lblTocTitle.Text = "📖 Mục lục bài học";
            // 
            // panelContent
            // 
            this.panelContent.Controls.Add(this.webView);
            this.panelContent.Controls.Add(this.panelBottom);
            this.panelContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelContent.Location = new System.Drawing.Point(250, 50);
            this.panelContent.Name = "panelContent";
            this.panelContent.Padding = new System.Windows.Forms.Padding(20);
            this.panelContent.Size = new System.Drawing.Size(550, 650);
            this.panelContent.TabIndex = 2;
            // 
            // webView
            // 
            this.webView.AllowExternalDrop = true;
            this.webView.CreationProperties = null;
            this.webView.DefaultBackgroundColor = System.Drawing.Color.White;
            this.webView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webView.Location = new System.Drawing.Point(20, 20);
            this.webView.Name = "webView";
            this.webView.Size = new System.Drawing.Size(510, 550);
            this.webView.TabIndex = 1;
            this.webView.ZoomFactor = 1D;
            // 
            // panelBottom
            // 
            this.panelBottom.Controls.Add(this.btnNext);
            this.panelBottom.Controls.Add(this.btnPrev);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(20, 570);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(510, 60);
            this.panelBottom.TabIndex = 0;
            // 
            // btnNext
            // 
            this.btnNext.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNext.FlatAppearance.BorderSize = 0;
            this.btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNext.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.btnNext.Location = new System.Drawing.Point(350, 10);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(150, 40);
            this.btnNext.TabIndex = 1;
            this.btnNext.Text = "Bài tiếp theo ▶";
            this.btnNext.UseVisualStyleBackColor = true;
            // 
            // btnPrev
            // 
            this.btnPrev.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPrev.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.btnPrev.Location = new System.Drawing.Point(10, 10);
            this.btnPrev.Name = "btnPrev";
            this.btnPrev.Size = new System.Drawing.Size(120, 40);
            this.btnPrev.TabIndex = 0;
            this.btnPrev.Text = "◀ Bài trước";
            this.btnPrev.UseVisualStyleBackColor = true;
            // 
            // LessonViewerControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panelContent);
            this.Controls.Add(this.panelSidebar);
            this.Controls.Add(this.panelTop);
            this.Name = "LessonViewerControl";
            this.Size = new System.Drawing.Size(800, 700);
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.panelSidebar.ResumeLayout(false);
            this.panelContent.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.webView)).EndInit();
            this.panelBottom.ResumeLayout(false);
            this.ResumeLayout(false);

        }
    }
}
