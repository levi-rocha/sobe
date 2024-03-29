using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

public interface IZipService
{
    Task<Stream> Zip(Stream input, string fileName);
}

public class LocalZipService : IZipService
{
    private string TempPath => Path.GetTempPath();

    public async Task<Stream> Zip(Stream input, string fileName)
    {
        var tempId = new Guid().ToString();
        var tempFile = Path.Combine(TempPath, "SOBE", tempId);
        var zipFile = $"{tempFile}.zip";
        using (var fs = File.OpenWrite(tempFile))
        {
            await input.CopyToAsync(fs);
        }
        using (var fs = File.Open(zipFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            using (var zipArchive = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                var entry = zipArchive.CreateEntryFromFile(tempFile, fileName);
            }
        }
        File.Delete(tempFile);
        return new DeleteOnDisposeFileStream(zipFile);
    }
}