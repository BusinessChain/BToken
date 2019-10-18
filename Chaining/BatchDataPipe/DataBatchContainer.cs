using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public abstract class DataBatchContainer
  {
    public bool IsValid = true;
    public int Index;
    public DataBatch Batch;
    public int CountItems;
    public byte[] Buffer;

    public Stopwatch StopwatchParse = new Stopwatch();


    protected DataBatchContainer(int index)
    {
      Index = index;
    }

    protected DataBatchContainer(int index, byte[] buffer)
    {
      Index = index;
      Buffer = buffer;
    }

    protected DataBatchContainer(
      DataBatch batch)
    {
      Batch = batch;
    }


    public abstract void Parse();
  }
}
