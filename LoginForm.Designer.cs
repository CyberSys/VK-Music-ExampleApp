namespace VK_Music
{
    partial class LoginForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LoginForm));
            codeeloGradientPanel1 = new CodeeloUI.Controls.CodeeloGradientPanel();
            codeeloTextBox3 = new CodeeloUI.Controls.CodeeloTextBox();
            label1 = new Label();
            authButton = new CodeeloUI.Controls.CodeeloButton();
            codeeloTextBox2 = new CodeeloUI.Controls.CodeeloTextBox();
            codeeloTextBox1 = new CodeeloUI.Controls.CodeeloTextBox();
            pictureBox2 = new PictureBox();
            pictureBox1 = new PictureBox();
            codeeloGradientPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // codeeloGradientPanel1
            // 
            codeeloGradientPanel1.AccessibleRole = null;
            codeeloGradientPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            codeeloGradientPanel1.CausesValidation = false;
            codeeloGradientPanel1.ColorFillFirst = Color.FromArgb(32, 32, 48);
            codeeloGradientPanel1.ColorFillSecond = Color.FromArgb(78, 81, 97);
            codeeloGradientPanel1.Controls.Add(codeeloTextBox3);
            codeeloGradientPanel1.Controls.Add(label1);
            codeeloGradientPanel1.Controls.Add(authButton);
            codeeloGradientPanel1.Controls.Add(codeeloTextBox2);
            codeeloGradientPanel1.Controls.Add(codeeloTextBox1);
            codeeloGradientPanel1.Controls.Add(pictureBox2);
            codeeloGradientPanel1.Controls.Add(pictureBox1);
            codeeloGradientPanel1.Dock = DockStyle.Fill;
            codeeloGradientPanel1.DrawGradient = true;
            codeeloGradientPanel1.GradientDirection = System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal;
            codeeloGradientPanel1.Location = new Point(0, 0);
            codeeloGradientPanel1.Margin = new Padding(4, 3, 4, 3);
            codeeloGradientPanel1.Name = "codeeloGradientPanel1";
            codeeloGradientPanel1.Size = new Size(782, 231);
            codeeloGradientPanel1.TabIndex = 0;
            // 
            // codeeloTextBox3
            // 
            codeeloTextBox3.BackColor = Color.FromArgb(78, 81, 97);
            codeeloTextBox3.BorderColor = Color.FromArgb(246, 195, 59);
            codeeloTextBox3.BorderFocusColor = Color.FromArgb(255, 152, 0);
            codeeloTextBox3.BorderSize = 2;
            codeeloTextBox3.Font = new Font("Roboto", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            codeeloTextBox3.ForeColor = Color.WhiteSmoke;
            codeeloTextBox3.Location = new Point(245, 165);
            codeeloTextBox3.Margin = new Padding(5);
            codeeloTextBox3.Multiline = false;
            codeeloTextBox3.Name = "codeeloTextBox3";
            codeeloTextBox3.Padding = new Padding(8);
            codeeloTextBox3.PlaceholderColor = Color.Gray;
            codeeloTextBox3.PlaceholderText = "Код двухфакторной аутентификации";
            codeeloTextBox3.Size = new Size(150, 40);
            codeeloTextBox3.TabIndex = 6;
            codeeloTextBox3.UnderlinedStyle = true;
            codeeloTextBox3.UsePasswordChar = false;
            codeeloTextBox3.Visible = false;
            codeeloTextBox3.KeyPress += codeeloTextBox3_KeyPress;
            // 
            // label1
            // 
            label1.BackColor = Color.Transparent;
            label1.Image = (Image)resources.GetObject("label1.Image");
            label1.Location = new Point(760, 1);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(21, 21);
            label1.TabIndex = 5;
            label1.Click += label1_Click;
            // 
            // authButton
            // 
            authButton.AccessibleRole = null;
            authButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            authButton.BackColor = Color.Transparent;
            authButton.BorderRadius = 20;
            authButton.BorderSize = 3;
            authButton.CausesValidation = false;
            authButton.ColorFillFirst = Color.FromArgb(32, 32, 48);
            authButton.ColorFillSecond = Color.FromArgb(78, 81, 97);
            authButton.DialogResult = false;
            authButton.FlatAppearance.BorderSize = 0;
            authButton.FlatStyle = FlatStyle.Flat;
            authButton.Font = new Font("Roboto", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            authButton.ForeColor = Color.WhiteSmoke;
            authButton.GradientBorderColorFirst = Color.FromArgb(246, 195, 59);
            authButton.GradientBorderColorSecond = Color.SpringGreen;
            authButton.GradientBorderDirection = System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal;
            authButton.GradientDirection = System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal;
            authButton.Location = new Point(404, 165);
            authButton.Margin = new Padding(4, 3, 4, 3);
            authButton.Name = "authButton";
            authButton.OnClickFirstBorderColor = Color.FromArgb(200, 150, 10);
            authButton.OnClickFirstFillColor = Color.FromArgb(32, 32, 48);
            authButton.OnClickSecondBorderColor = Color.FromArgb(60, 195, 70);
            authButton.OnClickSecondFillColor = Color.FromArgb(78, 81, 97);
            authButton.OnOverFirstBorderColor = Color.FromArgb(226, 175, 39);
            authButton.OnOverFirstFillColor = Color.FromArgb(32, 32, 48);
            authButton.OnOverSecondBorderColor = Color.FromArgb(30, 225, 97);
            authButton.OnOverSecondFillColor = Color.FromArgb(78, 81, 97);
            authButton.Size = new Size(133, 52);
            authButton.TabIndex = 1;
            authButton.TabStop = false;
            authButton.Text = "Вход";
            authButton.TextAlign = CodeeloUI.Enums.TextPosition.Center;
            authButton.UseGradient = true;
            authButton.UseGradientBorder = true;
            authButton.UseMnemonic = false;
            authButton.UseVisualStyleBackColor = false;
            authButton.Click += authButton_Click;
            // 
            // codeeloTextBox2
            // 
            codeeloTextBox2.BackColor = Color.FromArgb(78, 81, 97);
            codeeloTextBox2.BorderColor = Color.FromArgb(246, 195, 59);
            codeeloTextBox2.BorderFocusColor = Color.FromArgb(255, 152, 0);
            codeeloTextBox2.BorderSize = 2;
            codeeloTextBox2.Font = new Font("Roboto", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            codeeloTextBox2.ForeColor = Color.WhiteSmoke;
            codeeloTextBox2.Location = new Point(245, 97);
            codeeloTextBox2.Margin = new Padding(5);
            codeeloTextBox2.Multiline = false;
            codeeloTextBox2.Name = "codeeloTextBox2";
            codeeloTextBox2.Padding = new Padding(8);
            codeeloTextBox2.PlaceholderColor = Color.Gray;
            codeeloTextBox2.PlaceholderText = "Пароль";
            codeeloTextBox2.Size = new Size(292, 40);
            codeeloTextBox2.TabIndex = 3;
            codeeloTextBox2.UnderlinedStyle = true;
            codeeloTextBox2.UsePasswordChar = true;
            // 
            // codeeloTextBox1
            // 
            codeeloTextBox1.BackColor = Color.FromArgb(78, 81, 97);
            codeeloTextBox1.BorderColor = Color.FromArgb(246, 195, 59);
            codeeloTextBox1.BorderFocusColor = Color.FromArgb(255, 152, 0);
            codeeloTextBox1.BorderSize = 2;
            codeeloTextBox1.Font = new Font("Roboto", 14.25F, FontStyle.Regular, GraphicsUnit.Point);
            codeeloTextBox1.ForeColor = Color.WhiteSmoke;
            codeeloTextBox1.Location = new Point(245, 44);
            codeeloTextBox1.Margin = new Padding(5);
            codeeloTextBox1.Multiline = false;
            codeeloTextBox1.Name = "codeeloTextBox1";
            codeeloTextBox1.Padding = new Padding(8);
            codeeloTextBox1.PlaceholderColor = Color.Gray;
            codeeloTextBox1.PlaceholderText = "VK логин";
            codeeloTextBox1.Size = new Size(292, 40);
            codeeloTextBox1.TabIndex = 2;
            codeeloTextBox1.UnderlinedStyle = true;
            codeeloTextBox1.UsePasswordChar = false;
            // 
            // pictureBox2
            // 
            pictureBox2.BackColor = Color.Transparent;
            pictureBox2.Image = (Image)resources.GetObject("pictureBox2.Image");
            pictureBox2.Location = new Point(545, 33);
            pictureBox2.Margin = new Padding(4, 3, 4, 3);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(223, 210);
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.TabIndex = 1;
            pictureBox2.TabStop = false;
            // 
            // pictureBox1
            // 
            pictureBox1.BackColor = Color.Transparent;
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(14, 33);
            pictureBox1.Margin = new Padding(4, 3, 4, 3);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(223, 210);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // LoginForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(32, 32, 48);
            ClientSize = new Size(782, 231);
            Controls.Add(codeeloGradientPanel1);
            FormBorderStyle = FormBorderStyle.None;
            Margin = new Padding(4, 3, 4, 3);
            Name = "LoginForm";
            Opacity = 0.9D;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Form1";
            codeeloGradientPanel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private CodeeloUI.Controls.CodeeloGradientPanel codeeloGradientPanel1;
        private PictureBox pictureBox2;
        private PictureBox pictureBox1;
        private Label label1;
        private CodeeloUI.Controls.CodeeloButton authButton;
        private CodeeloUI.Controls.CodeeloTextBox codeeloTextBox2;
        private CodeeloUI.Controls.CodeeloTextBox codeeloTextBox1;
        private CodeeloUI.Controls.CodeeloTextBox codeeloTextBox3;
    }
}