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

    bool TryInsertDataContainer(ItemBatchContainer dataContainer);

    Task ArchiveBatchAsync(DataBatch batch);

    ItemBatchContainer LoadDataArchive(int archiveIndex);

    void LoadImage(out int archiveIndexNext);
  }
}
