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

    Task ArchiveBatch(DataBatch batch);

    DataBatch LoadDataBatch(int batchIndex);

    void LoadImage(out int archiveIndexNext);
  }
}
