using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public interface IStorageService
{
    Task WriteAsync(Stream stream, string filePath);
    Task<Stream> GetAsStreamAsync(string filePath);
    bool Exists(string filePath);
    Task DeleteAsync(string filePath);
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

    public bool Exists(string filePath)
    {
        var fullPath = GetFullPath(filePath);
        return Directory.Exists(fullPath);
    }

    public string GetFullPath(string filePath)
        => Path.Combine(BASE_PATH, filePath);

    public async Task<Stream> GetAsStreamAsync(string filePath)
    {
        var fullPath = GetFullPath(filePath);
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

    public async Task WriteAsync(Stream stream, string filePath)
    {
        var fullPath = GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        using (var fs = File.OpenWrite(fullPath))
        {
            await stream.CopyToAsync(fs);
        }
    }

    public Task DeleteAsync(string filePath)
    {
        var fullPath = GetFullPath(filePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.FromResult(0);
    }
}