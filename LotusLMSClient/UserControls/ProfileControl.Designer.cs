namespace LotusLMSClient.UserControls
{
    partial class ProfileControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Panel panelInfo;
        private System.Windows.Forms.Panel panelAvatar;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.Label lblRole;
        private System.Windows.Forms.Label lblCccd;
        private System.Windows.Forms.Label lblCccdVal;
        private System.Windows.Forms.Label lblPhone;
        private System.Windows.Forms.Label lblPhoneVal;
        private System.Windows.Forms.Button btnChangePass;

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
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.panelInfo = new System.Windows.Forms.Panel();
            this.btnChangePass = new System.Windows.Forms.Button();
            this.tableLayoutPanelDetails = new System.Windows.Forms.TableLayoutPanel();
            this.lblCccd = new System.Windows.Forms.Label();
            this.lblCccdVal = new System.Windows.Forms.Label();
            this.lblPhone = new System.Windows.Forms.Label();
            this.lblPhoneVal = new System.Windows.Forms.Label();
            this.lblName = new System.Windows.Forms.Label();
            this.lblRole = new System.Windows.Forms.Label();
            this.panelAvatar = new System.Windows.Forms.Panel();
            this.panelInfo.SuspendLayout();
            this.tableLayoutPanelDetails.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 20F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(25, 25);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(232, 37);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "👤 Hồ sơ cá nhân";
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblSubtitle.Location = new System.Drawing.Point(27, 70);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(277, 20);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Quản lý thông tin và bảo mật tài khoản.";
            // 
            // panelInfo
            // 
            this.panelInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelInfo.Controls.Add(this.btnChangePass);
            this.panelInfo.Controls.Add(this.tableLayoutPanelDetails);
            this.panelInfo.Controls.Add(this.lblName);
            this.panelInfo.Controls.Add(this.lblRole);
            this.panelInfo.Controls.Add(this.panelAvatar);
            this.panelInfo.Location = new System.Drawing.Point(26, 110);
            this.panelInfo.Name = "panelInfo";
            this.panelInfo.Padding = new System.Windows.Forms.Padding(20);
            this.panelInfo.Size = new System.Drawing.Size(748, 280);
            this.panelInfo.TabIndex = 2;
            // 
            // btnChangePass
            // 
            this.btnChangePass.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnChangePass.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChangePass.FlatAppearance.BorderSize = 0;
            this.btnChangePass.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnChangePass.Location = new System.Drawing.Point(553, 220);
            this.btnChangePass.Name = "btnChangePass";
            this.btnChangePass.Size = new System.Drawing.Size(160, 40);
            this.btnChangePass.TabIndex = 7;
            this.btnChangePass.Text = "Đổi mật khẩu";
            this.btnChangePass.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanelDetails
            // 
            this.tableLayoutPanelDetails.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelDetails.ColumnCount = 2;
            this.tableLayoutPanelDetails.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.tableLayoutPanelDetails.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelDetails.Controls.Add(this.lblCccd, 0, 0);
            this.tableLayoutPanelDetails.Controls.Add(this.lblCccdVal, 1, 0);
            this.tableLayoutPanelDetails.Controls.Add(this.lblPhone, 0, 1);
            this.tableLayoutPanelDetails.Controls.Add(this.lblPhoneVal, 1, 1);
            this.tableLayoutPanelDetails.Location = new System.Drawing.Point(155, 120);
            this.tableLayoutPanelDetails.Name = "tableLayoutPanelDetails";
            this.tableLayoutPanelDetails.RowCount = 2;
            this.tableLayoutPanelDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanelDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanelDetails.Size = new System.Drawing.Size(550, 80);
            this.tableLayoutPanelDetails.TabIndex = 6;
            // 
            // lblCccd
            // 
            this.lblCccd.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCccd.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblCccd.Location = new System.Drawing.Point(3, 0);
            this.lblCccd.Name = "lblCccd";
            this.lblCccd.Size = new System.Drawing.Size(144, 40);
            this.lblCccd.TabIndex = 0;
            this.lblCccd.Text = "CCCD/CMT/Mã:";
            this.lblCccd.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblCccdVal
            // 
            this.lblCccdVal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCccdVal.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblCccdVal.Location = new System.Drawing.Point(153, 0);
            this.lblCccdVal.Name = "lblCccdVal";
            this.lblCccdVal.Size = new System.Drawing.Size(394, 40);
            this.lblCccdVal.TabIndex = 1;
            this.lblCccdVal.Text = "033200004602";
            this.lblCccdVal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblPhone
            // 
            this.lblPhone.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPhone.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblPhone.Location = new System.Drawing.Point(3, 40);
            this.lblPhone.Name = "lblPhone";
            this.lblPhone.Size = new System.Drawing.Size(144, 40);
            this.lblPhone.TabIndex = 2;
            this.lblPhone.Text = "Số điện thoại:";
            this.lblPhone.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblPhoneVal
            // 
            this.lblPhoneVal.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPhoneVal.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblPhoneVal.Location = new System.Drawing.Point(153, 40);
            this.lblPhoneVal.Name = "lblPhoneVal";
            this.lblPhoneVal.Size = new System.Drawing.Size(394, 40);
            this.lblPhoneVal.TabIndex = 3;
            this.lblPhoneVal.Text = "0901234567";
            this.lblPhoneVal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblName
            // 
            this.lblName.AutoSize = true;
            this.lblName.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.lblName.Location = new System.Drawing.Point(150, 40);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(148, 32);
            this.lblName.TabIndex = 1;
            this.lblName.Text = "Bùi Gia Yên";
            // 
            // lblRole
            // 
            this.lblRole.AutoSize = true;
            this.lblRole.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblRole.ForeColor = System.Drawing.Color.Gray;
            this.lblRole.Location = new System.Drawing.Point(152, 78);
            this.lblRole.Name = "lblRole";
            this.lblRole.Size = new System.Drawing.Size(134, 20);
            this.lblRole.TabIndex = 2;
            this.lblRole.Text = "Học viên (Student)";
            // 
            // panelAvatar
            // 
            this.panelAvatar.Location = new System.Drawing.Point(30, 30);
            this.panelAvatar.Name = "panelAvatar";
            this.panelAvatar.Size = new System.Drawing.Size(100, 100);
            this.panelAvatar.TabIndex = 0;
            this.panelAvatar.Paint += new System.Windows.Forms.PaintEventHandler(this.panelAvatar_Paint);
            // 
            // ProfileControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panelInfo);
            this.Controls.Add(this.lblSubtitle);
            this.Controls.Add(this.lblTitle);
            this.Name = "ProfileControl";
            this.Size = new System.Drawing.Size(800, 700);
            this.panelInfo.ResumeLayout(false);
            this.panelInfo.PerformLayout();
            this.tableLayoutPanelDetails.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelDetails;
    }
}
