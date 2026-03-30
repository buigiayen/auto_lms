namespace LotusLMSClient.UserControls
{
    partial class DashboardControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label lblWelcome;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutCards;
        private System.Windows.Forms.Panel panelCard1;
        private System.Windows.Forms.Panel panelCard2;
        private System.Windows.Forms.Panel panelCard3;
        private System.Windows.Forms.Panel panelCard4;
        private System.Windows.Forms.Label lblSection1;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelCourses;
        private System.Windows.Forms.Label lblSection2;
        private System.Windows.Forms.DataGridView dgvLichHoc;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDate;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColTitle;

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
            this.lblWelcome = new System.Windows.Forms.Label();
            this.flowLayoutCards = new System.Windows.Forms.FlowLayoutPanel();
            this.panelCard1 = new System.Windows.Forms.Panel();
            this.panelCard2 = new System.Windows.Forms.Panel();
            this.panelCard3 = new System.Windows.Forms.Panel();
            this.panelCard4 = new System.Windows.Forms.Panel();
            this.lblSection1 = new System.Windows.Forms.Label();
            this.flowLayoutPanelCourses = new System.Windows.Forms.FlowLayoutPanel();
            this.lblSection2 = new System.Windows.Forms.Label();
            this.dgvLichHoc = new System.Windows.Forms.DataGridView();
            this.ColDate = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColTitle = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.flowLayoutCards.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvLichHoc)).BeginInit();
            this.SuspendLayout();
            // 
            // lblWelcome
            // 
            this.lblWelcome.AutoSize = true;
            this.lblWelcome.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Regular);
            this.lblWelcome.Location = new System.Drawing.Point(30, 20);
            this.lblWelcome.Name = "lblWelcome";
            this.lblWelcome.Size = new System.Drawing.Size(262, 32);
            this.lblWelcome.TabIndex = 0;
            this.lblWelcome.Text = "Xin chào, 👋 Bùi Gia Yên!";
            // 
            // flowLayoutCards
            // 
            this.flowLayoutCards.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutCards.Controls.Add(this.panelCard1);
            this.flowLayoutCards.Controls.Add(this.panelCard2);
            this.flowLayoutCards.Controls.Add(this.panelCard3);
            this.flowLayoutCards.Controls.Add(this.panelCard4);
            this.flowLayoutCards.Location = new System.Drawing.Point(30, 80);
            this.flowLayoutCards.Name = "flowLayoutCards";
            this.flowLayoutCards.Size = new System.Drawing.Size(730, 100);
            this.flowLayoutCards.TabIndex = 1;
            // 
            // panelCard1
            // 
            this.panelCard1.Location = new System.Drawing.Point(3, 3);
            this.panelCard1.Name = "panelCard1";
            this.panelCard1.Size = new System.Drawing.Size(170, 80);
            this.panelCard1.TabIndex = 0;
            // 
            // panelCard2
            // 
            this.panelCard2.Location = new System.Drawing.Point(179, 3);
            this.panelCard2.Name = "panelCard2";
            this.panelCard2.Size = new System.Drawing.Size(170, 80);
            this.panelCard2.TabIndex = 1;
            // 
            // panelCard3
            // 
            this.panelCard3.Location = new System.Drawing.Point(355, 3);
            this.panelCard3.Name = "panelCard3";
            this.panelCard3.Size = new System.Drawing.Size(170, 80);
            this.panelCard3.TabIndex = 2;
            // 
            // panelCard4
            // 
            this.panelCard4.Location = new System.Drawing.Point(531, 3);
            this.panelCard4.Name = "panelCard4";
            this.panelCard4.Size = new System.Drawing.Size(170, 80);
            this.panelCard4.TabIndex = 3;
            // 
            // lblSection1
            // 
            this.lblSection1.AutoSize = true;
            this.lblSection1.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSection1.Location = new System.Drawing.Point(30, 200);
            this.lblSection1.Name = "lblSection1";
            this.lblSection1.Size = new System.Drawing.Size(183, 25);
            this.lblSection1.TabIndex = 2;
            this.lblSection1.Text = "Khóa học đang học";
            // 
            // flowLayoutPanelCourses
            // 
            this.flowLayoutPanelCourses.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelCourses.Location = new System.Drawing.Point(30, 240);
            this.flowLayoutPanelCourses.Name = "flowLayoutPanelCourses";
            this.flowLayoutPanelCourses.Size = new System.Drawing.Size(730, 150);
            this.flowLayoutPanelCourses.TabIndex = 3;
            // 
            // lblSection2
            // 
            this.lblSection2.AutoSize = true;
            this.lblSection2.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSection2.Location = new System.Drawing.Point(30, 410);
            this.lblSection2.Name = "lblSection2";
            this.lblSection2.Size = new System.Drawing.Size(150, 25);
            this.lblSection2.TabIndex = 4;
            this.lblSection2.Text = "Lịch học sắp tới";
            // 
            // dgvLichHoc
            // 
            this.dgvLichHoc.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvLichHoc.AllowUserToAddRows = false;
            this.dgvLichHoc.AllowUserToDeleteRows = false;
            this.dgvLichHoc.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvLichHoc.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvLichHoc.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColDate,
            this.ColTime,
            this.ColTitle});
            this.dgvLichHoc.Location = new System.Drawing.Point(30, 450);
            this.dgvLichHoc.Name = "dgvLichHoc";
            this.dgvLichHoc.ReadOnly = true;
            this.dgvLichHoc.RowHeadersVisible = false;
            this.dgvLichHoc.Size = new System.Drawing.Size(730, 200);
            this.dgvLichHoc.TabIndex = 5;
            // 
            // ColDate
            // 
            this.ColDate.HeaderText = "Ngày";
            this.ColDate.Name = "ColDate";
            this.ColDate.ReadOnly = true;
            // 
            // ColTime
            // 
            this.ColTime.HeaderText = "Giờ";
            this.ColTime.Name = "ColTime";
            this.ColTime.ReadOnly = true;
            // 
            // ColTitle
            // 
            this.ColTitle.HeaderText = "Tên buổi học";
            this.ColTitle.Name = "ColTitle";
            this.ColTitle.ReadOnly = true;
            // 
            // DashboardControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dgvLichHoc);
            this.Controls.Add(this.lblSection2);
            this.Controls.Add(this.flowLayoutPanelCourses);
            this.Controls.Add(this.lblSection1);
            this.Controls.Add(this.flowLayoutCards);
            this.Controls.Add(this.lblWelcome);
            this.Name = "DashboardControl";
            this.Size = new System.Drawing.Size(800, 700);
            this.flowLayoutCards.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvLichHoc)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}
