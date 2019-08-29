using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  interface IDatabase
  {
    DataBatch CreateBatch();
    void InsertBatch(DataBatch batch);
    Task ArchiveBatchAsync(DataBatch batch);
    DataBatch LoadBatchFromArchive(int batchIndex);
    int LoadImage();
  }
}
