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
      this.textBox_LocatorHash = new System.Windows.Forms.TextBox();
      this.SuspendLayout();
      // 
      // getHeadersButton
      // 
      this.getHeadersButton.Location = new System.Drawing.Point(13, 163);
      this.getHeadersButton.Name = "getHeadersButton";
      this.getHeadersButton.Size = new System.Drawing.Size(75, 23);
      this.getHeadersButton.TabIndex = 1;
      this.getHeadersButton.Text = "GetHeaders";
      this.getHeadersButton.UseVisualStyleBackColor = true;
      this.getHeadersButton.Click += new System.EventHandler(this.getHeadersButton_Click);
      // 
      // textBox_LocatorHash
      // 
      this.textBox_LocatorHash.Location = new System.Drawing.Point(13, 192);
      this.textBox_LocatorHash.Name = "textBox_LocatorHash";
      this.textBox_LocatorHash.Size = new System.Drawing.Size(391, 20);
      this.textBox_LocatorHash.TabIndex = 2;
      this.textBox_LocatorHash.Text = "00000000000000000024ee96acb254c7847c2f4c792570845e1e0203dc26872e";
      // 
      // Form1
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(467, 262);
      this.Controls.Add(this.textBox_LocatorHash);
      this.Controls.Add(this.getHeadersButton);
      this.Name = "Form1";
      this.Text = "Form1";
      this.ResumeLayout(false);
      this.PerformLayout();

        }

        #endregion
    private System.Windows.Forms.Button getHeadersButton;
    private System.Windows.Forms.TextBox textBox_LocatorHash;
  }
}

