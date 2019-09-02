using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public abstract class DataBatch
  {
    public bool IsValid;
    public int Index;
    public List<ItemBatchContainer> ItemBatchContainers;
    public int CountItems;
    public bool IsFinalBatch;

    public DataBatch(int index)
    {
      Index = index;
      ItemBatchContainers = new List<ItemBatchContainer>();
    }

    public abstract void Parse();
  }

  public abstract class ItemBatchContainer
  {
    public string Label;
    public int CountItems;
    public byte[] Buffer;

    protected ItemBatchContainer(
      int countItems,
      byte[] buffer)
    {
      CountItems = countItems;
      Buffer = buffer;
    }
  }
}
