using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SOBE.Services
{
    public interface IStorageService
    {
        Task<string> DownloadFromUrlAsync(string fileUrl, string filePath);
        Task WriteAsync(Stream stream, string filePath);
        Task<Stream> GetAsStreamAsync(string filePath);
        bool Exists(string path);
        Task DeleteAsync(string filePath);
    }

    public class LocalStorageService : IStorageService
    {
        private readonly static string BASE_PATH = Path.Combine(Path.GetTempPath(), "SOBE");

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public LocalStorageService(ILogger<LocalStorageService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            if (!Directory.Exists(BASE_PATH))
                Directory.CreateDirectory(BASE_PATH);
        }

        public async Task<string> DownloadFromUrlAsync(string fileUrl, string filePath)
        {
            var extension = await GetExtensionFromUrl(fileUrl);
            var responseStream = await _httpClient.GetStreamAsync(fileUrl);
            string sha1 = string.Empty;
            var tempName = new Guid().ToString();
            var basePath = GetFullPath(filePath);
            var path = Path.Combine(basePath, tempName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = File.Create(path))
            {
                await responseStream.CopyToAsync(fs);
                fs.Seek(0, SeekOrigin.Begin);
                sha1 = CalculateSHA1(fs);
                _logger.LogDebug($"Calculated SHA1: {sha1}");
            }
            var permPath = Path.Combine(basePath, $"{sha1}{extension}");
            File.Move(path, permPath);
            return sha1;
        }

        private async Task<string> GetExtensionFromUrl(string fileUrl)
        {
            var res = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, fileUrl));
            var absoluteUri = res.RequestMessage.RequestUri.AbsoluteUri.Split('?')[0];
            var splitByPeriod = absoluteUri.Split('.');
            var extension = $".{splitByPeriod[splitByPeriod.Length - 1]}";
            return extension;
        }

        private static string CalculateSHA1(FileStream fs)
        {
            string sha1;
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

            return sha1;
        }

        public bool Exists(string path)
        {
            var fullPath = GetFullPath(path);
            var exists = File.Exists(fullPath) || Directory.Exists(path);
            _logger.LogDebug($"{(exists ? "Exists" : "Does not exist:")}: {path}");
            return exists;
        }

        public string GetFullPath(string filePath)
            => Path.Combine(BASE_PATH, filePath);

        public Task<Stream> GetAsStreamAsync(string filePath)
        {
            var fullPath = GetFullPath(filePath);
            _logger.LogDebug($"fullPath: {fullPath}");
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(); //todo: replace with custom exception
            }
            return Task.FromResult((Stream)(File.OpenRead(fullPath)));
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

        public Task DeleteAsync(string path)
        {
            var fullPath = GetFullPath(path);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            return Task.FromResult(0);
        }
    }
}
