using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Blockchain
{
  partial class Headerchain
  {
    public class HeaderBranch
    {
      public bool IsFork;
      public Header HeaderForkTip;
      public bool IsForkTipInserted;
      public bool IsHeaderTipInserted;
      public Header HeaderRoot;
      public Header HeaderTip;
      public Header HeaderLastInserted;
      public List<double> HeaderDifficulties = new List<double>();
      public double AccumulatedDifficultyInserted;
      public int HeightInserted;


      public HeaderBranch(
        Header headerRoot,
        Header headerTip)
      {
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      public void ReportHeaderInsertion(Header header)
      {
        HeaderLastInserted = header;

        if (header == HeaderForkTip)
        {
          IsForkTipInserted = true;
        }

        if (header == HeaderTip)
        {
          IsHeaderTipInserted = true;
        }

        AccumulatedDifficultyInserted +=
          HeaderDifficulties[HeightInserted++];
      }
    };
  }
}
