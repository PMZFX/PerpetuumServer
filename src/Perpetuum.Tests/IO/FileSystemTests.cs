using System;
using System.IO;
using Xunit;

namespace Perpetuum.Tests.IO
{
    public class FileSystemTests
    {
        [Fact]
        public void MoveFileOverwritesAnExistingTarget()
        {
            string root = Path.Combine(Path.GetTempPath(), "perpetuum-filesystem-" + Guid.NewGuid());
            Directory.CreateDirectory(root);

            try
            {
                string source = Path.Combine(root, "layer.tmp");
                string target = Path.Combine(root, "layer.bin");
                File.WriteAllText(source, "new layer");
                File.WriteAllText(target, "old layer");
                var fileSystem = new Perpetuum.IO.FileSystem(root);

                fileSystem.MoveFile("layer.tmp", "layer.bin");

                Assert.False(File.Exists(source));
                Assert.Equal("new layer", File.ReadAllText(target));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
