namespace BToken
{
    partial class Form1
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
      this.getHeadersButton = new System.Windows.Forms.Button();
      this.button_ping = new System.Windows.Forms.Button();
      this.button_GetBlock = new System.Windows.Forms.Button();
      this.SuspendLayout();
      // 
      // getHeadersButton
      // 
      this.getHeadersButton.Location = new System.Drawing.Point(13, 49);
      this.getHeadersButton.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
      this.getHeadersButton.Name = "getHeadersButton";
      this.getHeadersButton.Size = new System.Drawing.Size(100, 28);
      this.getHeadersButton.TabIndex = 1;
      this.getHeadersButton.Text = "GetHeaders";
      this.getHeadersButton.UseVisualStyleBackColor = true;
      this.getHeadersButton.Click += new System.EventHandler(this.getHeadersButton_Click);
      // 
      // button_ping
      // 
      this.button_ping.Location = new System.Drawing.Point(13, 13);
      this.button_ping.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
      this.button_ping.Name = "button_ping";
      this.button_ping.Size = new System.Drawing.Size(100, 28);
      this.button_ping.TabIndex = 3;
      this.button_ping.Text = "Ping!";
      this.button_ping.UseVisualStyleBackColor = true;
      this.button_ping.Click += new System.EventHandler(this.button_ping_Click);
      // 
      // button_GetBlock
      // 
      this.button_GetBlock.Location = new System.Drawing.Point(13, 85);
      this.button_GetBlock.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
      this.button_GetBlock.Name = "button_GetBlock";
      this.button_GetBlock.Size = new System.Drawing.Size(100, 28);
      this.button_GetBlock.TabIndex = 4;
      this.button_GetBlock.Text = "GetBlock";
      this.button_GetBlock.UseVisualStyleBackColor = true;
      this.button_GetBlock.Click += new System.EventHandler(this.button_GetBlock_Click);
      // 
      // Form1
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(140, 116);
      this.Controls.Add(this.button_GetBlock);
      this.Controls.Add(this.button_ping);
      this.Controls.Add(this.getHeadersButton);
      this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
      this.Name = "Form1";
      this.Opacity = 0D;
      this.Text = "Form1";
      this.ResumeLayout(false);

        }

        #endregion
    private System.Windows.Forms.Button getHeadersButton;
    private System.Windows.Forms.Button button_ping;
    private System.Windows.Forms.Button button_GetBlock;
  }
}

