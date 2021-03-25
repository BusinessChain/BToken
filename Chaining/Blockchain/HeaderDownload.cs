using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class HeaderDownload
  {
    public List<Header> Locator;

    public int CountHeaders;
    public Header HeaderTip;
    public Header HeaderRoot;
    
    public void InsertHeader(Header header)
    {
      if(CountHeaders == 0)
      {
        HeaderRoot = header;
        HeaderTip = header;

        Header headerLocatorAncestor =
          Locator.Find(
            h => h.Hash.IsEqual(
              header.HashPrevious));

        if (headerLocatorAncestor == null)
        {
          throw new ProtocolException(
            "GetHeaders does not connect to locator.");
        }

        HeaderRoot.HeaderPrevious = headerLocatorAncestor;
      }
      else
      {
        if (!HeaderTip.Hash.IsEqual(header.HashPrevious))
        {
          throw new ProtocolException(
            string.Format(
              "Header insertion out of order. " +
              "Previous header {0}\n Next header: {1}",
              HeaderTip.Hash.ToString(),
              header.HashPrevious.ToString()));
        }

        header.HeaderPrevious = HeaderTip;
        HeaderTip.HeaderNext = header;
        HeaderTip = header;
      }

      CountHeaders += 1;
    }

    public void Reset()
    {
      CountHeaders = 0;
      HeaderTip = null;
      HeaderRoot = null;
    }
  }
}
