namespace LotusLMSClient.UserControls
{
    partial class ExamDetailControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Button btnBack;
        private System.Windows.Forms.Label lblDeadline;
        private System.Windows.Forms.Label lblProgressTitle;
        private System.Windows.Forms.ProgressBar progressTime;
        private System.Windows.Forms.Label lblPercent;
        private System.Windows.Forms.FlowLayoutPanel flpSummary;
        private System.Windows.Forms.Label lblSummaryPrefix;
        private System.Windows.Forms.Label lblSummaryCurrent;
        private System.Windows.Forms.Label lblSummaryMax;
        private System.Windows.Forms.Label lblSummaryStatus;
        private System.Windows.Forms.Label lblTableTitle;
        private System.Windows.Forms.DataGridView dgvScores;
        private System.Windows.Forms.Panel panelHeader;

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
            this.btnBack = new System.Windows.Forms.Button();
            this.lblDeadline = new System.Windows.Forms.Label();
            this.lblProgressTitle = new System.Windows.Forms.Label();
            this.progressTime = new System.Windows.Forms.ProgressBar();
            this.lblPercent = new System.Windows.Forms.Label();
            this.lblTableTitle = new System.Windows.Forms.Label();
            this.dgvScores = new System.Windows.Forms.DataGridView();
            this.panelHeader = new System.Windows.Forms.Panel();
            this.flpSummary = new System.Windows.Forms.FlowLayoutPanel();
            this.lblSummaryPrefix = new System.Windows.Forms.Label();
            this.lblSummaryCurrent = new System.Windows.Forms.Label();
            this.lblSummaryMax = new System.Windows.Forms.Label();
            this.lblSummaryStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dgvScores)).BeginInit();
            this.SuspendLayout();
            // 
            // btnBack
            // 
            this.btnBack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBack.Location = new System.Drawing.Point(20, 10);
            this.btnBack.Name = "btnBack";
            this.btnBack.Size = new System.Drawing.Size(100, 30);
            this.btnBack.TabIndex = 0;
            this.btnBack.Text = "⬅ Quay lại";
            this.btnBack.UseVisualStyleBackColor = true;
            // 
            // lblDeadline
            // 
            this.lblDeadline.AutoSize = true;
            this.lblDeadline.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.lblDeadline.Location = new System.Drawing.Point(20, 60);
            this.lblDeadline.Name = "lblDeadline";
            this.lblDeadline.Size = new System.Drawing.Size(262, 21);
            this.lblDeadline.TabIndex = 1;
            this.lblDeadline.Text = "Hoàn thành bài học trước --/--/----";
            // 
            // lblProgressTitle
            // 
            this.lblProgressTitle.AutoSize = true;
            this.lblProgressTitle.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.lblProgressTitle.Location = new System.Drawing.Point(20, 100);
            this.lblProgressTitle.Name = "lblProgressTitle";
            this.lblProgressTitle.Size = new System.Drawing.Size(171, 21);
            this.lblProgressTitle.TabIndex = 2;
            this.lblProgressTitle.Text = "Tiến độ (tính theo thời gian)";
            // 
            // progressTime
            // 
            this.progressTime.Location = new System.Drawing.Point(24, 130);
            this.progressTime.Name = "progressTime";
            this.progressTime.Size = new System.Drawing.Size(400, 15);
            this.progressTime.TabIndex = 3;
            // 
            // lblPercent
            // 
            this.lblPercent.AutoSize = true;
            this.lblPercent.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblPercent.Location = new System.Drawing.Point(430, 128);
            this.lblPercent.Name = "lblPercent";
            this.lblPercent.Size = new System.Drawing.Size(46, 19);
            this.lblPercent.TabIndex = 4;
            this.lblPercent.Text = "44.4%";
            // 
            // flpSummary
            // 
            this.flpSummary.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.flpSummary.AutoSize = true;
            this.flpSummary.Controls.Add(this.lblSummaryPrefix);
            this.flpSummary.Controls.Add(this.lblSummaryCurrent);
            this.flpSummary.Controls.Add(this.lblSummaryMax);
            this.flpSummary.Controls.Add(this.lblSummaryStatus);
            this.flpSummary.Location = new System.Drawing.Point(200, 170);
            this.flpSummary.Name = "flpSummary";
            this.flpSummary.Size = new System.Drawing.Size(400, 30);
            this.flpSummary.TabIndex = 5;
            this.flpSummary.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            // 
            // lblSummaryPrefix
            // 
            this.lblSummaryPrefix.AutoSize = true;
            this.lblSummaryPrefix.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSummaryPrefix.Location = new System.Drawing.Point(3, 0);
            this.lblSummaryPrefix.Name = "lblSummaryPrefix";
            this.lblSummaryPrefix.Size = new System.Drawing.Size(71, 25);
            this.lblSummaryPrefix.Text = "Số giờ";
            // 
            // lblSummaryCurrent
            // 
            this.lblSummaryCurrent.AutoSize = true;
            this.lblSummaryCurrent.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSummaryCurrent.Location = new System.Drawing.Point(80, 0);
            this.lblSummaryCurrent.Name = "lblSummaryCurrent";
            this.lblSummaryCurrent.Size = new System.Drawing.Size(50, 25);
            this.lblSummaryCurrent.ForeColor = System.Drawing.Color.Red;
            this.lblSummaryCurrent.Text = "0.00";
            // 
            // lblSummaryMax
            // 
            this.lblSummaryMax.AutoSize = true;
            this.lblSummaryMax.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSummaryMax.Location = new System.Drawing.Point(135, 0);
            this.lblSummaryMax.Name = "lblSummaryMax";
            this.lblSummaryMax.Size = new System.Drawing.Size(50, 25);
            this.lblSummaryMax.Text = "/ 25";
            // 
            // lblSummaryStatus
            // 
            this.lblSummaryStatus.AutoSize = true;
            this.lblSummaryStatus.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblSummaryStatus.Location = new System.Drawing.Point(190, 0);
            this.lblSummaryStatus.Name = "lblSummaryStatus";
            this.lblSummaryStatus.Size = new System.Drawing.Size(150, 25);
            this.lblSummaryStatus.Text = " - ❌ Chưa đạt";
            // 
            // lblTableTitle
            // 
            this.lblTableTitle.AutoSize = true;
            this.lblTableTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblTableTitle.Location = new System.Drawing.Point(20, 220);
            this.lblTableTitle.Name = "lblTableTitle";
            this.lblTableTitle.Size = new System.Drawing.Size(217, 25);
            this.lblTableTitle.TabIndex = 6;
            this.lblTableTitle.Text = "Điểm thành phần chi tiết";
            // 
            // dgvScores
            // 
            this.dgvScores.AllowUserToAddRows = false;
            this.dgvScores.AllowUserToDeleteRows = false;
            this.dgvScores.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvScores.BackgroundColor = System.Drawing.Color.White;
            this.dgvScores.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvScores.Location = new System.Drawing.Point(20, 260);
            this.dgvScores.Name = "dgvScores";
            this.dgvScores.ReadOnly = true;
            this.dgvScores.RowHeadersVisible = false;
            this.dgvScores.Size = new System.Drawing.Size(760, 420);
            this.dgvScores.TabIndex = 7;
            // 
            // ExamDetailControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dgvScores);
            this.Controls.Add(this.lblTableTitle);
            this.Controls.Add(this.flpSummary);
            this.Controls.Add(this.lblPercent);
            this.Controls.Add(this.progressTime);
            this.Controls.Add(this.lblProgressTitle);
            this.Controls.Add(this.lblDeadline);
            this.Controls.Add(this.btnBack);
            this.Name = "ExamDetailControl";
            this.Size = new System.Drawing.Size(800, 700);
            ((System.ComponentModel.ISupportInitialize)(this.dgvScores)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}
