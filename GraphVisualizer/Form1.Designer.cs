namespace GraphVisualizer
{
    partial class Form1
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
            components = new System.ComponentModel.Container();
            renderTimer = new System.Windows.Forms.Timer(components);
            btnBFS = new Button();
            btnDFS = new Button();
            btnAStar = new Button();
            btnRegen = new Button();
            SuspendLayout();
            // 
            // renderTimer
            // 
            renderTimer.Interval = 5;
            renderTimer.Tick += renderTimer_Tick;
            // 
            // btnBFS
            // 
            btnBFS.Location = new Point(1151, 36);
            btnBFS.Name = "btnBFS";
            btnBFS.Size = new Size(112, 34);
            btnBFS.TabIndex = 0;
            btnBFS.Text = "BFS";
            btnBFS.UseVisualStyleBackColor = true;
            btnBFS.Click += button1_Click;
            // 
            // btnDFS
            // 
            btnDFS.Location = new Point(1151, 104);
            btnDFS.Name = "btnDFS";
            btnDFS.Size = new Size(112, 34);
            btnDFS.TabIndex = 1;
            btnDFS.Text = "DFS";
            btnDFS.UseVisualStyleBackColor = true;
            btnDFS.Click += btnDFS_Click;
            // 
            // btnAStar
            // 
            btnAStar.Location = new Point(1151, 181);
            btnAStar.Name = "btnAStar";
            btnAStar.Size = new Size(112, 34);
            btnAStar.TabIndex = 2;
            btnAStar.Text = "A Star";
            btnAStar.UseVisualStyleBackColor = true;
            btnAStar.Click += btnAStar_Click;
            // 
            // btnRegen
            // 
            btnRegen.Location = new Point(1151, 379);
            btnRegen.Name = "btnRegen";
            btnRegen.Size = new Size(112, 34);
            btnRegen.TabIndex = 3;
            btnRegen.Text = "REGEN";
            btnRegen.UseVisualStyleBackColor = true;
            btnRegen.Click += btnRegen_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1384, 970);
            Controls.Add(btnRegen);
            Controls.Add(btnAStar);
            Controls.Add(btnDFS);
            Controls.Add(btnBFS);
            Name = "Form1";
            Text = "Form1";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Timer renderTimer;
        private Button btnBFS;
        private Button btnDFS;
        private Button btnAStar;
        private Button btnRegen;
    }
}