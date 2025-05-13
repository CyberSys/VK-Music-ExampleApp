namespace VK_Music
{
    partial class Mp3StreamingPanel
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            buttonPlay = new Button();
            textBoxStreamingUrl = new TextBox();
            timer1 = new System.Windows.Forms.Timer(components);
            label1 = new Label();
            progressBarBuffer = new ProgressBar();
            label2 = new Label();
            buttonPause = new Button();
            buttonStop = new Button();
            labelBuffered = new Label();
            labelVolume = new Label();
            volumeSlider1 = new NAudio.Gui.VolumeSlider();
            buttonNext = new Button();
            buttonBack = new Button();
            SuspendLayout();
            // 
            // buttonPlay
            // 
            buttonPlay.Location = new Point(104, 113);
            buttonPlay.Margin = new Padding(4);
            buttonPlay.Name = "buttonPlay";
            buttonPlay.Size = new Size(88, 26);
            buttonPlay.TabIndex = 0;
            buttonPlay.Text = "Play";
            buttonPlay.UseVisualStyleBackColor = true;
            buttonPlay.Click += buttonPlay_Click;
            // 
            // textBoxStreamingUrl
            // 
            textBoxStreamingUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBoxStreamingUrl.Location = new Point(113, 14);
            textBoxStreamingUrl.Margin = new Padding(4);
            textBoxStreamingUrl.Name = "textBoxStreamingUrl";
            textBoxStreamingUrl.Size = new Size(367, 23);
            textBoxStreamingUrl.TabIndex = 1;
            textBoxStreamingUrl.Text = "https://jfm1.hostingradio.ru:14536/metal.mp3";
            // 
            // timer1
            // 
            timer1.Interval = 250;
            timer1.Tick += timer1_Tick;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(14, 17);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(88, 15);
            label1.TabIndex = 2;
            label1.Text = "Streaming URL:";
            // 
            // progressBarBuffer
            // 
            progressBarBuffer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBarBuffer.Location = new Point(113, 45);
            progressBarBuffer.Margin = new Padding(4);
            progressBarBuffer.Name = "progressBarBuffer";
            progressBarBuffer.Size = new Size(329, 26);
            progressBarBuffer.TabIndex = 3;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(14, 52);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(55, 15);
            label2.TabIndex = 4;
            label2.Text = "Buffered:";
            // 
            // buttonPause
            // 
            buttonPause.Location = new Point(200, 113);
            buttonPause.Margin = new Padding(4);
            buttonPause.Name = "buttonPause";
            buttonPause.Size = new Size(88, 26);
            buttonPause.TabIndex = 5;
            buttonPause.Text = "Pause";
            buttonPause.UseVisualStyleBackColor = true;
            buttonPause.Click += buttonPause_Click;
            // 
            // buttonStop
            // 
            buttonStop.Location = new Point(392, 113);
            buttonStop.Margin = new Padding(4);
            buttonStop.Name = "buttonStop";
            buttonStop.Size = new Size(88, 26);
            buttonStop.TabIndex = 6;
            buttonStop.Text = "Stop";
            buttonStop.UseVisualStyleBackColor = true;
            buttonStop.Click += buttonStop_Click;
            // 
            // labelBuffered
            // 
            labelBuffered.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelBuffered.AutoSize = true;
            labelBuffered.Location = new Point(448, 52);
            labelBuffered.Margin = new Padding(4, 0, 4, 0);
            labelBuffered.Name = "labelBuffered";
            labelBuffered.Size = new Size(27, 15);
            labelBuffered.TabIndex = 7;
            labelBuffered.Text = "0.0s";
            // 
            // labelVolume
            // 
            labelVolume.AutoSize = true;
            labelVolume.Location = new Point(14, 84);
            labelVolume.Margin = new Padding(4, 0, 4, 0);
            labelVolume.Name = "labelVolume";
            labelVolume.Size = new Size(50, 15);
            labelVolume.TabIndex = 8;
            labelVolume.Text = "Volume:";
            // 
            // volumeSlider1
            // 
            volumeSlider1.Location = new Point(113, 80);
            volumeSlider1.Margin = new Padding(4);
            volumeSlider1.Name = "volumeSlider1";
            volumeSlider1.Size = new Size(127, 22);
            volumeSlider1.TabIndex = 9;
            // 
            // buttonNext
            // 
            buttonNext.Location = new Point(296, 113);
            buttonNext.Margin = new Padding(4);
            buttonNext.Name = "buttonNext";
            buttonNext.Size = new Size(88, 26);
            buttonNext.TabIndex = 10;
            buttonNext.Text = "Next";
            buttonNext.UseVisualStyleBackColor = true;
            buttonNext.Click += buttonNext_Click;
            // 
            // buttonBack
            // 
            buttonBack.Location = new Point(8, 113);
            buttonBack.Margin = new Padding(4);
            buttonBack.Name = "buttonBack";
            buttonBack.Size = new Size(88, 26);
            buttonBack.TabIndex = 11;
            buttonBack.Text = "Back";
            buttonBack.UseVisualStyleBackColor = true;
            buttonBack.MouseClick += buttonBack_MouseClick;
            // 
            // Mp3StreamingPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(buttonBack);
            Controls.Add(buttonNext);
            Controls.Add(volumeSlider1);
            Controls.Add(labelVolume);
            Controls.Add(labelBuffered);
            Controls.Add(buttonStop);
            Controls.Add(buttonPause);
            Controls.Add(label2);
            Controls.Add(progressBarBuffer);
            Controls.Add(label1);
            Controls.Add(textBoxStreamingUrl);
            Controls.Add(buttonPlay);
            Margin = new Padding(4);
            Name = "Mp3StreamingPanel";
            Size = new Size(494, 156);
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button buttonPlay;
        private System.Windows.Forms.TextBox textBoxStreamingUrl;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ProgressBar progressBarBuffer;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button buttonPause;
        private System.Windows.Forms.Button buttonStop;
        private System.Windows.Forms.Label labelBuffered;
        private System.Windows.Forms.Label labelVolume;
        private NAudio.Gui.VolumeSlider volumeSlider1;
        private Button buttonNext;
        private Button buttonBack;
    }
}