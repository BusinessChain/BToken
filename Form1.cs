using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net;

namespace BToken
{
  public partial class Form1 : Form
  {
    Bitcoin Node;


    public Form1()
    {
      InitializeComponent();
      try
      {
        Node = new Bitcoin();
        Task runNodeTask = Node.startAsync();
      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Format("Ups, something went wrong: '{0}'", ex.Message));
      }
    }
    
    private async void getHeadersButton_Click(object sender, EventArgs e)
    {
      await Node.Network.GetHeadersAsync(Node.Blockchain.GetBlockLocator());
    }

    private async void button_ping_Click(object sender, EventArgs e)
    {
      await Node.Network.PingAsync();
    }
  }
}