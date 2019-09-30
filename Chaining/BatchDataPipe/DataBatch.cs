﻿using System;
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
    public List<ItemBatchContainer> ItemBatchContainers;
    public int CountItems;
    public bool IsFinalBatch;
    

    
    public DataBatch(int index)
    {
      Index = index;
      ItemBatchContainers = new List<ItemBatchContainer>();
    }

    public void Parse()
    {
      foreach (ItemBatchContainer container in ItemBatchContainers)
      {
        container.Parse();
        CountItems += container.CountItems;
      }
    }
  }

  public abstract class ItemBatchContainer
  {
    public bool IsValid;
    public int Index;
    public DataBatch Batch;
    public int CountItems;
    public byte[] Buffer;

    public Stopwatch StopwatchParse = new Stopwatch();



    protected ItemBatchContainer(
      DataBatch batch,
      byte[] buffer)
    {
      Batch = batch;
      Buffer = buffer;
    }

    protected ItemBatchContainer(int index, byte[] buffer)
    {
      Index = index;
      Buffer = buffer;
    }

    protected ItemBatchContainer(
      DataBatch batch)
    {
      Batch = batch;
    }


    public abstract void Parse();
  }
}