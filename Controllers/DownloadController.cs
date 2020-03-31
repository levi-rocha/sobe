using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SOBE.Models;

namespace SOBE.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
        private const string VIRUSTOTAL_APIKEY = "ec544f93a52305cafa0273d4d3ac54e89db0d94a82468b5a9130ef1bb1e9b7ba";
        private const string ENDPOINT_VIRUSTOTAL_REPORT = "https://www.virustotal.com/vtapi/v2/file/report";
        private const string ENDPOINT_VIRUSTOTAL_SCAN = "https://www.virustotal.com/vtapi/v2/file/scan";
        private IWebHostEnvironment _env;
        private string _storagePath;

        public DownloadController(IWebHostEnvironment env)
        {
            _env = env;
            _storagePath = Path.GetTempPath();
        }

        [HttpGet, Route("status")]
        public ActionResult<RequestHandle> GetStatus([FromQuery]string requestId)
        {
            var dirPath = AbsPath(requestId);
            var flag = Path.Combine(dirPath, "work.done");
            if (System.IO.File.Exists(flag))
                return Ok(new RequestHandle { Id = new Guid(requestId), Ready = true });
            else if (System.IO.Directory.Exists(dirPath))
                return Ok(new RequestHandle { Id = new Guid(requestId) });
            else
                return NotFound();
        }

        [HttpGet]
        public async Task<ActionResult> Get([FromQuery]string requestId)
        {
            var dirPath = AbsPath(requestId);
            var flag = Path.Combine(dirPath, "work.done");
            if (System.IO.File.Exists(flag))
            {
                var path = await System.IO.File.ReadAllTextAsync(flag);
                var stream = System.IO.File.OpenRead(path);
                return File(stream, "application/octet-stream");
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        public ActionResult<RequestHandle> Post(DownloadRequest downloadRequest)
        {
            var outputName = downloadRequest.OutputName ?? Path.GetFileName(downloadRequest.FileUrl);
            var requestId = Guid.NewGuid();
            var downloadDir = System.IO.Directory.CreateDirectory(AbsPath(requestId.ToString()));
            var result = new RequestHandle { Id = requestId, Ready = false };

            Task.Run(async () => await ProcessFile(downloadRequest.FileUrl, downloadDir.FullName, outputName)).ContinueWith((r) =>
            {
                if (r.IsCompletedSuccessfully)
                    Console.WriteLine($"[ProcessFile] [{outputName}] Success");
                else
                {
                    foreach (var exception in r.Exception.InnerExceptions)
                    {
                        Console.Error.WriteLine($"Exception in [ProcessFile] [{outputName}]: {exception.Message}{Environment.NewLine} Stack Trace: {exception.StackTrace}");
                    }
                }
            });

            return Ok(result);
        }

        public class RequestHandle
        {
            public Guid Id { get; set; }
            public bool Ready { get; set; }
        }

        private async Task ProcessFile(string fileUrl, string downloadDir, string outputName)
        {
            var srcPath = Path.Combine(downloadDir, "src");
            System.IO.Directory.CreateDirectory(srcPath);
            var downloadedPath = Path.Combine(srcPath, outputName);
            var zippedPath = Path.Combine(downloadDir, $"{Path.GetFileNameWithoutExtension(outputName)}.zip");

            // Download
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(fileUrl);
                if (response.IsSuccessStatusCode)
                {
                    using (var fileStream = new FileStream(downloadedPath, FileMode.CreateNew))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
            }

            // Scan
            string sha1;
            using (FileStream fs = new FileStream(downloadedPath, FileMode.Open))
            using (BufferedStream bs = new BufferedStream(fs))
            {
                using (SHA1Managed sha1managed = new SHA1Managed())
                {
                    byte[] hash = sha1managed.ComputeHash(bs);
                    StringBuilder formatted = new StringBuilder(2 * hash.Length);
                    foreach (byte b in hash)
                    {
                        formatted.AppendFormat("{0:X2}", b);
                    }
                    sha1 = formatted.ToString();
                }
            }

            await Scan(sha1, downloadedPath);

            // Zip
            ZipFile.CreateFromDirectory(Path.Combine(downloadDir, "src"), zippedPath);

            // Cleanup
            System.IO.Directory.Delete(srcPath, recursive: true);
            await System.IO.File.WriteAllTextAsync(Path.Combine(downloadDir, "work.done"), zippedPath);
        }

        private static async Task Scan(string sha1, string filePath, int maxAttempts = 10)
        {
            bool uploaded = false;
            bool isSafe;
            int attempts = 0;
            do
            {
                attempts++;
                if (TryScan(sha1, out isSafe))
                {
                    if (!isSafe)
                        throw new Exception("Detected as virus by VirusTotal");
                    else
                        return;
                }
                else
                {
                    if (!uploaded)
                    {
                        await UploadForScan(filePath);
                        uploaded = true;
                    }
                    // wait for scan
                    Thread.Sleep(10000);
                }
            } while (attempts < maxAttempts);
        }

        private static async Task UploadForScan(string filePath)
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(VIRUSTOTAL_APIKEY), "apikey");
            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            content.Add(new ByteArrayContent((fileBytes), 0, fileBytes.Length), "file");
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(ENDPOINT_VIRUSTOTAL_SCAN, content);
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Could not upload to VirusTotal: {response.Content.ReadAsStringAsync().Result}");
            }
        }

        private static bool TryScan(string sha1, out bool isSafe)
        {
            try
            { isSafe = IsSafe(sha1); return true; }
            catch
            { isSafe = false; return false; }
        }

        private static bool IsSafe(string sha1)
        {
            dynamic response;
            using (var client = new HttpClient())
            {
                var urlReport = $"{ENDPOINT_VIRUSTOTAL_REPORT}?resource={sha1}&apikey={VIRUSTOTAL_APIKEY}";
                response = JsonConvert.DeserializeObject<dynamic>(client.GetAsync(urlReport).Result.Content.ReadAsStringAsync().Result);
            }
            if (response.response_code == 1)
                return response.positives == 0;
            else
                throw new Exception($"VirusTotal error! Response code: {response.response_code} | Message: {response.verbose_msg}");
        }

        public string AbsPath(string fileName) => Path.Combine(_storagePath, fileName);
    }
}