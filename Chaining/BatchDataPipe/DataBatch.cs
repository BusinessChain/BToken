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
    public bool IsValid = true;


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
        IsValid &= container.TryParse();
        CountItems += container.CountItems;
      }

      IsValid &= (CountItems == 0);
    }
  }
}
