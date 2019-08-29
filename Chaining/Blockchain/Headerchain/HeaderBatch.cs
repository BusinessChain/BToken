using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class HeaderBatch : DataBatch
  {
    public HeaderBatch(int index) 
      : base(index)
    { }

    public HeaderBatch(
      List<Header> headers, 
      byte[] headerBytes, 
      int index)
      : base(index)
    {
      ItemBatchContainers.Add(
        new HeaderBatchContainer(headers, headerBytes));
    }

    public override void Parse()
    {
      // HeaderBatch is already fully parsed at its creation
    }

    public byte[] GetHeaderHashPrevious()
    {
      return ((HeaderBatchContainer)ItemBatchContainers.First())
        .Headers
        .First()
        .HashPrevious;
    }

    public byte[] GetHeaderHashLast()
    {
      return ((HeaderBatchContainer)ItemBatchContainers.Last())
        .Headers
        .Last()
        .HeaderHash;
    }
  }

  class HeaderBatchContainer : ItemBatchContainer
  {
    public List<Header> Headers;

    public HeaderBatchContainer(List<Header> headers, byte[] headerBytes)
      : base(
          headers.Count, 
          headerBytes)
    {
      Headers = headers;
    }
  }
}
