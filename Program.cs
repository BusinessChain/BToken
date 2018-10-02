using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using BToken.Bitcoin;

namespace BToken
{
  static class Program
  {
    public static BitcoinNode Node = new BitcoinNode();

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
      try
      {
        Task startNodeTask = Node.StartAsync();
      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Format("Ups, something went wrong: '{0}'", ex.Message));
      }

      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new Form1());
    }
  }
}
