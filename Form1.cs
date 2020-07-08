using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public Form1()
    {
      InitializeComponent();
    }
    
    private async void getHeadersButton_Click(object sender, EventArgs e)
    {
      List<UInt256> headers = new List<UInt256>() { new UInt256("0000000000000000001b25d108e90678516c91cf26332e44cd616655e56d1467") };
      //await Node.Network.GetHeadersAsync(headers);
      //await Program.Node.Network.GetHeadersAsync(Program.Node.Blockchain.GetBlockLocations().Select(b => b.Hash).ToList());
    }

    private async void button_ping_Click(object sender, EventArgs e)
    {
      //await Program.Node.Network.PingAsync();
    }

    private async void button_GetBlock_Click(object sender, EventArgs e)
    {
      //NetworkBlock block = await Program.Node.Network.GetBlockAsync(
      //  new UInt256("000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f")
      //  );
    }
  }
}