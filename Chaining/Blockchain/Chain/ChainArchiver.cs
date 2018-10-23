using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Chain
    {
      class ChainArchiver
      {
        readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HeaderArchive");
        static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

        FileStream FileHeaderchain;


        public ChainArchiver()
        {

        }

        static FileStream OpenHeaderchainFile()
        {
          string filePath = Path.Combine(RootDirectory.Name);

          return new FileStream(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        }
      }
    }
  }
}
