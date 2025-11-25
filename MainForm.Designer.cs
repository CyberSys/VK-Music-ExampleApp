namespace VK_Music
{
    partial class MainForm
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
            dataGridView1 = new DataGridView();
            panel1 = new Panel();
            listBoxPlugins = new ListBox();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            SuspendLayout();
            // 
            // dataGridView1
            // 
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Location = new Point(3, 67);
            dataGridView1.Margin = new Padding(4, 3, 4, 3);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.RowTemplate.Height = 23;
            dataGridView1.Size = new Size(344, 378);
            dataGridView1.TabIndex = 0;
            // 
            // panel1
            // 
            panel1.Location = new Point(354, 67);
            panel1.Name = "panel1";
            panel1.Size = new Size(585, 378);
            panel1.TabIndex = 1;
            // 
            // listBoxPlugins
            // 
            listBoxPlugins.FormattingEnabled = true;
            listBoxPlugins.ItemHeight = 15;
            listBoxPlugins.Location = new Point(3, 12);
            listBoxPlugins.Name = "listBoxPlugins";
            listBoxPlugins.Size = new Size(529, 49);
            listBoxPlugins.TabIndex = 2;
            listBoxPlugins.DoubleClick += listBoxPlugins_DoubleClick;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(933, 519);
            Controls.Add(dataGridView1);
            Controls.Add(listBoxPlugins);
            Controls.Add(panel1);
            Margin = new Padding(4, 3, 4, 3);
            Name = "MainForm";
            Text = "MainForm";
            Load += MainForm_Load;
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private DataGridView dataGridView1;
        private Panel panel1;
        private ListBox listBoxPlugins;
    }
}