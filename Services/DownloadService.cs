using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SOBE.Services
{
    public interface IDownloadService
    {
        Task<string> DownloadAsync(string fileUrl, string fileName);
    }

    public class LocalDownloadService : IDownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly LocalStorageService _storageService;

        public LocalDownloadService(HttpClient httpClient, LocalStorageService storageService)
        {
            _httpClient = httpClient;
            _storageService = storageService;
        }

        public async Task<string> DownloadAsync(string fileUrl, string fileName)
        {
            var response = await _httpClient.GetAsync(fileUrl);
            string sha1 = string.Empty;
            if (response.IsSuccessStatusCode)
            {
                var path = _storageService.GetFullPath(fileName);
                using (var fs = File.Create(path))
                {
                    await response.Content.CopyToAsync(fs);
                    using (SHA1Managed sha1managed = new SHA1Managed())
                    {
                        byte[] hash = sha1managed.ComputeHash(fs);
                        var formatted = new StringBuilder(2 * hash.Length);
                        foreach (byte b in hash)
                        {
                            formatted.AppendFormat("{0:X2}", b);
                        }
                        sha1 = formatted.ToString();
                    }
                }
            }
            return sha1;
        }
    }
}
