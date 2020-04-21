using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public interface IStorageService
{
    Task WriteAsync(Stream stream, string fileName, string path = null);
    Task<Stream> GetAsStreamAsync(string fileName, string path = null);
    bool Exists(string fileName, string path = null);
}

public class LocalStorageService : IStorageService
{
    private readonly static string BASE_PATH = Path.Combine(Path.GetTempPath(), "SOBE");

    private readonly ILogger _logger;

    public LocalStorageService(ILogger<LocalStorageService> logger)
    {
        _logger = logger;
        if (!Directory.Exists(BASE_PATH))
            Directory.CreateDirectory(BASE_PATH);
    }

    public bool Exists(string fileName, string path = null)
    {
        var fullPath = GetFullPath(fileName, path);
        return Directory.Exists(fullPath);
    }

    // Will not work if path has more than 1 folders. Fix only if worth the time
    public string GetFullPath(string fileName, string path)
    {
        var basePath = string.IsNullOrWhiteSpace(path) ? BASE_PATH : Path.Combine(BASE_PATH, path);
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);
        return Path.Combine(basePath, fileName);
    }

    public async Task<Stream> GetAsStreamAsync(string fileName, string path = null)
    {
        var fullPath = GetFullPath(fileName, path);
        _logger.LogDebug($"fullPath: {fullPath}");
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(); //todo: replace with custom exception
        }
        var memoryStream = new MemoryStream();
        using (var fs = File.OpenRead(fullPath))
        {
            await fs.CopyToAsync(memoryStream);
        }
        return memoryStream;
    }

    public async Task WriteAsync(Stream stream, string fileName, string path = null)
    {
        var fullPath = GetFullPath(fileName, path);
        using (var fs = File.OpenWrite(fullPath))
        {
            await stream.CopyToAsync(fs);
        }
    }
}