using System;
using System.Drawing;
using System.Windows.Forms;
using LotusLMSClient.Helpers;
using LotusLMSClient.Services;
using LotusLMSClient.UserControls;

namespace LotusLMSClient.Forms
{
    public partial class MainForm : Form
    {
        private readonly AuthService _authService;
        
        private Button _currentBtn;
        private Panel _leftBorderBtn;

        public MainForm(AuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(1024, 768);
            
            _leftBorderBtn = new Panel();
            _leftBorderBtn.Size = new Size(7, 50);
            panelSidebar.Controls.Add(_leftBorderBtn);
            
            SetupUI();
        }

        private void SetupUI()
        {
            panelSidebar.BackColor = ColorPalette.SidebarBg;
            panelHeader.BackColor = Color.White;
            panelContent.BackColor = ColorPalette.ContentBg;
            panelLogo.BackColor = ColorPalette.PrimaryDark;
            lblUsername.Text = string.IsNullOrEmpty(_authService.CurrentName) ? "👤 Học viên" : $"👤 {_authService.CurrentName}";
            lblUsername.ForeColor = ColorPalette.TextPrimary;
            lblLogo.ForeColor = Color.White;
            
            FormatNavButton(btnDashboard);
            FormatNavButton(btnCourses);
            FormatNavButton(btnExams);
            FormatNavButton(btnQnA);
            FormatNavButton(btnProfile);
            FormatNavButton(btnLogout);
            
            // Wire up remaining events
            btnProfile.Click += btnProfile_Click;
            btnExams.Click += btnExams_Click;
            btnQnA.Click += btnQnA_Click;
            
            // Default active
            btnDashboard.PerformClick();
        }

        private void FormatNavButton(Button btn)
        {
            btn.BackColor = ColorPalette.SidebarBg;
            btn.ForeColor = ColorPalette.SidebarText;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ColorPalette.SidebarHover;
            btn.FlatStyle = FlatStyle.Flat;
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.Padding = new Padding(20, 0, 0, 0);
            btn.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
        }

        private void ActivateButton(object senderBtn)
        {
            if (senderBtn != null)
            {
                DisableButton();
                _currentBtn = (Button)senderBtn;
                _currentBtn.BackColor = ColorPalette.SidebarHover;
                _currentBtn.ForeColor = Color.White;
                _currentBtn.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
                
                _leftBorderBtn.BackColor = ColorPalette.SidebarActive;
                _leftBorderBtn.Location = new Point(0, _currentBtn.Location.Y);
                _leftBorderBtn.Visible = true;
                _leftBorderBtn.BringToFront();
            }
        }

        private void DisableButton()
        {
            if (_currentBtn != null)
            {
                _currentBtn.BackColor = ColorPalette.SidebarBg;
                _currentBtn.ForeColor = ColorPalette.SidebarText;
                _currentBtn.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            }
        }

        private void LoadUserControl(UserControl uc)
        {
            panelContent.Controls.Clear();
            uc.Dock = DockStyle.Fill;
            panelContent.Controls.Add(uc);
        }

        private void btnDashboard_Click(object sender, EventArgs e)
        {
            ActivateButton(sender);
            LoadUserControl(new DashboardControl(_authService));
        }

        private void btnCourses_Click(object sender, EventArgs e)
        {
            ActivateButton(sender);
            LoadUserControl(new CourseListControl(_authService));
        }
        
        private void btnProfile_Click(object sender, EventArgs e)
        {
            ActivateButton(sender);
            LoadUserControl(new ProfileControl(_authService.CurrentName, _authService.CurrentCccd));
        }

        private void btnExams_Click(object sender, EventArgs e)
        {
            ActivateButton(sender);
            LoadUserControl(new SessionListControl()); // Reusing it as a placeholder or could show an under construction message
            // MessageBox.Show("Tính năng Đang phát triển");
        }

        private void btnQnA_Click(object sender, EventArgs e)
        {
            ActivateButton(sender);
            MessageBox.Show("Tính năng Hỏi đáp đang phát triển.");
        }
        
        private void btnLogout_Click(object sender, EventArgs e)
        {
            _authService.Logout();
            LoginForm loginForm = new LoginForm();
            loginForm.Show();
            this.Hide();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private bool _dragging = false;
        private Point _dragCursorPoint;
        private Point _dragFormPoint;

        private void panelHeader_MouseDown(object sender, MouseEventArgs e)
        {
            _dragging = true;
            _dragCursorPoint = Cursor.Position;
            _dragFormPoint = this.Location;
        }

        private void panelHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Point dif = Point.Subtract(Cursor.Position, new Size(_dragCursorPoint));
                this.Location = Point.Add(_dragFormPoint, new Size(dif));
            }
        }

        private void panelHeader_MouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }
    }
}
