using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BToken.Chaining
{
  public class DataBatch
  {
    public bool IsValid;
    public int Index;
    public List<DataBatchContainer> ItemBatchContainers;
    public int CountItems;
    public bool IsFinalBatch;
    

    
    public DataBatch(int index)
    {
      Index = index;
      ItemBatchContainers = new List<DataBatchContainer>();
    }
  }
}
