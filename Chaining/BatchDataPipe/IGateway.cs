using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  interface IGateway
  {
    Task Synchronize(DataBatch batchInsertedLast);
    void ReportInvalidBatch(DataBatch batch);
    Task StartListener();
  }
}
