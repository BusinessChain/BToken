using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using BToken.Networking;


namespace BToken.Chaining
{
  public class DataBatch
  {
    public int Index;
    public List<DataContainer> DataContainers =
      new List<DataContainer>();
    public int CountItems;
    public bool IsCancellationBatch;


    public DataBatch()
    { }

    public DataBatch(int index)
    {
      Index = index;
    }



    public void TryParse()
    {
      foreach(DataContainer container in DataContainers)
      {
        container.TryParse();
        CountItems += container.CountItems;
      }
    }
  }
}
