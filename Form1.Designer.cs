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
      this.SuspendLayout();
      // 
      // getHeadersButton
      // 
      this.getHeadersButton.Location = new System.Drawing.Point(197, 144);
      this.getHeadersButton.Name = "getHeadersButton";
      this.getHeadersButton.Size = new System.Drawing.Size(75, 23);
      this.getHeadersButton.TabIndex = 1;
      this.getHeadersButton.Text = "GetHeaders";
      this.getHeadersButton.UseVisualStyleBackColor = true;
      this.getHeadersButton.Click += new System.EventHandler(this.getHeadersButton_Click);
      // 
      // button_ping
      // 
      this.button_ping.Location = new System.Drawing.Point(197, 66);
      this.button_ping.Name = "button_ping";
      this.button_ping.Size = new System.Drawing.Size(75, 23);
      this.button_ping.TabIndex = 3;
      this.button_ping.Text = "Ping!";
      this.button_ping.UseVisualStyleBackColor = true;
      this.button_ping.Click += new System.EventHandler(this.button_ping_Click);
      // 
      // Form1
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(467, 262);
      this.Controls.Add(this.button_ping);
      this.Controls.Add(this.getHeadersButton);
      this.Name = "Form1";
      this.Text = "Form1";
      this.ResumeLayout(false);

        }

        #endregion
    private System.Windows.Forms.Button getHeadersButton;
    private System.Windows.Forms.Button button_ping;
  }
}

