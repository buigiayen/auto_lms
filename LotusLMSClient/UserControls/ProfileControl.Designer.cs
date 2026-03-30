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
            this.panelAvatar = new System.Windows.Forms.Panel();
            this.lblName = new System.Windows.Forms.Label();
            this.lblRole = new System.Windows.Forms.Label();
            this.lblCccd = new System.Windows.Forms.Label();
            this.lblCccdVal = new System.Windows.Forms.Label();
            this.lblPhone = new System.Windows.Forms.Label();
            this.lblPhoneVal = new System.Windows.Forms.Label();
            this.btnChangePass = new System.Windows.Forms.Button();
            this.panelInfo.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(20, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(207, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "👤 Hồ sơ cá nhân";
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblSubtitle.Location = new System.Drawing.Point(22, 60);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(260, 19);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Quản lý thông tin và bảo mật tài khoản.";
            // 
            // panelInfo
            // 
            this.panelInfo.Controls.Add(this.btnChangePass);
            this.panelInfo.Controls.Add(this.lblPhoneVal);
            this.panelInfo.Controls.Add(this.lblPhone);
            this.panelInfo.Controls.Add(this.lblCccdVal);
            this.panelInfo.Controls.Add(this.lblCccd);
            this.panelInfo.Controls.Add(this.lblRole);
            this.panelInfo.Controls.Add(this.lblName);
            this.panelInfo.Controls.Add(this.panelAvatar);
            this.panelInfo.Location = new System.Drawing.Point(26, 100);
            this.panelInfo.Name = "panelInfo";
            this.panelInfo.Size = new System.Drawing.Size(750, 250);
            this.panelInfo.TabIndex = 2;
            // 
            // panelAvatar
            // 
            this.panelAvatar.Location = new System.Drawing.Point(30, 30);
            this.panelAvatar.Name = "panelAvatar";
            this.panelAvatar.Size = new System.Drawing.Size(100, 100);
            this.panelAvatar.TabIndex = 0;
            this.panelAvatar.Paint += new System.Windows.Forms.PaintEventHandler(this.panelAvatar_Paint);
            // 
            // lblName
            // 
            this.lblName.AutoSize = true;
            this.lblName.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblName.Location = new System.Drawing.Point(150, 40);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(133, 30);
            this.lblName.TabIndex = 1;
            this.lblName.Text = "Bùi Gia Yên";
            // 
            // lblRole
            // 
            this.lblRole.AutoSize = true;
            this.lblRole.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblRole.ForeColor = System.Drawing.Color.Gray;
            this.lblRole.Location = new System.Drawing.Point(152, 75);
            this.lblRole.Name = "lblRole";
            this.lblRole.Size = new System.Drawing.Size(122, 19);
            this.lblRole.TabIndex = 2;
            this.lblRole.Text = "Học viên (Student)";
            // 
            // lblCccd
            // 
            this.lblCccd.AutoSize = true;
            this.lblCccd.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblCccd.Location = new System.Drawing.Point(150, 130);
            this.lblCccd.Name = "lblCccd";
            this.lblCccd.Size = new System.Drawing.Size(130, 21);
            this.lblCccd.TabIndex = 3;
            this.lblCccd.Text = "CCCD/CMT/Mã:";
            // 
            // lblCccdVal
            // 
            this.lblCccdVal.AutoSize = true;
            this.lblCccdVal.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.lblCccdVal.Location = new System.Drawing.Point(280, 130);
            this.lblCccdVal.Name = "lblCccdVal";
            this.lblCccdVal.Size = new System.Drawing.Size(109, 21);
            this.lblCccdVal.TabIndex = 4;
            this.lblCccdVal.Text = "033200004602";
            // 
            // lblPhone
            // 
            this.lblPhone.AutoSize = true;
            this.lblPhone.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblPhone.Location = new System.Drawing.Point(150, 170);
            this.lblPhone.Name = "lblPhone";
            this.lblPhone.Size = new System.Drawing.Size(115, 21);
            this.lblPhone.TabIndex = 5;
            this.lblPhone.Text = "Số điện thoại:";
            // 
            // lblPhoneVal
            // 
            this.lblPhoneVal.AutoSize = true;
            this.lblPhoneVal.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.lblPhoneVal.Location = new System.Drawing.Point(280, 170);
            this.lblPhoneVal.Name = "lblPhoneVal";
            this.lblPhoneVal.Size = new System.Drawing.Size(100, 21);
            this.lblPhoneVal.TabIndex = 6;
            this.lblPhoneVal.Text = "0901234567";
            // 
            // btnChangePass
            // 
            this.btnChangePass.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnChangePass.FlatAppearance.BorderSize = 0;
            this.btnChangePass.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnChangePass.Location = new System.Drawing.Point(550, 190);
            this.btnChangePass.Name = "btnChangePass";
            this.btnChangePass.Size = new System.Drawing.Size(150, 35);
            this.btnChangePass.TabIndex = 7;
            this.btnChangePass.Text = "Đổi mật khẩu";
            this.btnChangePass.UseVisualStyleBackColor = true;
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
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}
