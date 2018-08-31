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
      List<UInt256> headers = new List<UInt256>() { new UInt256("0000000000000000001b25d108e90678516c91cf26332e44cd616655e56d1467") };
      await Node.Network.GetHeadersAsync(headers);
      //await Node.Network.GetHeadersAsync(Node.Blockchain.GetHeaderLocator().Select(b => b.Hash).ToList());
    }

    private async void button_ping_Click(object sender, EventArgs e)
    {
      await Node.Network.PingAsync();
    }

    private async void button_GetBlock_Click(object sender, EventArgs e)
    {
      List<UInt256> headers = new List<UInt256>() {
        new UInt256("0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"),
        new UInt256("000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214"),
        new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39")
      };
      await Node.Network.GetBlockAsync(headers);

      // await Blocks
    }
  }
}