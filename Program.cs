using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BToken
{
  static class Program
  {
    public static BitcoinNode Node;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
      try
      {
        Node = new BitcoinNode();
        Task startNodeTask = Node.StartAsync();
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
      }

      //Application.EnableVisualStyles();
      //Application.SetCompatibleTextRenderingDefault(false);
      //Application.Run(new Form1());
    }
  }
}
