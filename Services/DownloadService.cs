using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

public interface IDownloadService
{
    Task<string> DownloadAsync(string fileUrl, string fileName, string destinationPath);
}

public class DownloadService : IDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly IStorageService _storageService;

    public DownloadService(HttpClient httpClient, IStorageService storageService)
    {
        _httpClient = httpClient;
        _storageService = storageService;
    }

    public async Task<string> DownloadAsync(string fileUrl, string fileName, string destinationPath)
    {
        var response = await _httpClient.GetAsync(fileUrl);
        var memoryStream = new MemoryStream();
        string sha1 = string.Empty;
        if (response.IsSuccessStatusCode)
        {
            await response.Content.CopyToAsync(memoryStream);
            using (SHA1Managed sha1managed = new SHA1Managed())
            {
                byte[] hash = sha1managed.ComputeHash(memoryStream);
                var formatted = new StringBuilder(2 * hash.Length);
                foreach (byte b in hash)
                {
                    formatted.AppendFormat("{0:X2}", b);
                }
                sha1 = formatted.ToString();
            }
            await _storageService.WriteAsync(memoryStream, fileName, destinationPath);
        }
        return sha1;
    }
}