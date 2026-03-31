namespace LotusLMSClient.UserControls
{
    partial class DashboardControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label lblWelcome;
        private System.Windows.Forms.Label lblSection1;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelCourses;

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
            lblWelcome = new System.Windows.Forms.Label();
            lblSection1 = new System.Windows.Forms.Label();
            flowLayoutPanelCourses = new System.Windows.Forms.FlowLayoutPanel();
            SuspendLayout();
            // 
            // lblWelcome
            // 
            lblWelcome.AutoSize = true;
            lblWelcome.Font = new System.Drawing.Font("Segoe UI", 18F);
            lblWelcome.Location = new System.Drawing.Point(35, 23);
            lblWelcome.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            lblWelcome.Name = "lblWelcome";
            lblWelcome.Size = new System.Drawing.Size(285, 32);
            lblWelcome.TabIndex = 0;
            lblWelcome.Text = "Xin chào, 👋 Bùi Gia Yên!";
            // 
            // lblSection1
            // 
            lblSection1.AutoSize = true;
            lblSection1.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            lblSection1.Location = new System.Drawing.Point(35, 70);
            lblSection1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            lblSection1.Name = "lblSection1";
            lblSection1.Size = new System.Drawing.Size(182, 25);
            lblSection1.TabIndex = 2;
            lblSection1.Text = "Khóa học đang học";
            // 
            // flowLayoutPanelCourses
            // 
            flowLayoutPanelCourses.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            flowLayoutPanelCourses.Location = new System.Drawing.Point(35, 98);
            flowLayoutPanelCourses.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            flowLayoutPanelCourses.Name = "flowLayoutPanelCourses";
            flowLayoutPanelCourses.Size = new System.Drawing.Size(1301, 511);
            flowLayoutPanelCourses.TabIndex = 3;
            // 
            // DashboardControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(flowLayoutPanelCourses);
            Controls.Add(lblSection1);
            Controls.Add(lblWelcome);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "DashboardControl";
            Size = new System.Drawing.Size(1382, 808);
            ResumeLayout(false);
            PerformLayout();

        }
    }
}
