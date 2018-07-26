using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Net;

namespace BToken
{
  public partial class Form1 : Form
  {
    public Form1()
    {
      InitializeComponent();
    }

    private void startButton_Click(object sender, EventArgs e)
    {
      Bitcoin node = new Bitcoin();
      node.startAsync();
    }
  }
}
