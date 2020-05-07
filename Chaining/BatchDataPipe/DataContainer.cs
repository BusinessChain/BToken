using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

using System.Security.Cryptography;


namespace BToken.Chaining
{
  public abstract class DataContainer
  {
    public bool IsValid = true;
    public int Index;


    
    protected DataContainer()
    { }

    protected DataContainer(int index)
    {
      Index = index;
    }

    protected DataContainer(byte[] buffer)
    {
      Buffer = buffer;
    }

    protected DataContainer(int index, byte[] buffer)
    {
      Index = index;
      Buffer = buffer;
    }

    

    public abstract void Parse(SHA256 sHA256);
  }
}
