using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  interface IDatabase
  {
    bool TryInsertBatch(DataBatch batch, out ItemBatchContainer containerInvalid);
    bool TryInsertContainer(ItemBatchContainer container);

    Task ArchiveBatch(DataBatch batch);

    ItemBatchContainer LoadDataContainer(int containerIndex);

    void LoadImage(out int archiveIndexNext);
  }
}
