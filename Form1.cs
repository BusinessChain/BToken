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
      Node = new Bitcoin();
      Task runNodeTask = Node.startAsync();
    }
    
    private async void getHeadersButton_Click(object sender, EventArgs e)
    {
      string hashString = textBox_LocatorHash.Text;
      UInt256 hash = new UInt256(hashString);
      Console.WriteLine("Send 'getheaders', locator = " + hashString);
      await Node.NetworkAdapter.GetHeadersAsync(hash);
    }

  }
}
