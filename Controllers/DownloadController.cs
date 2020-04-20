using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SOBE.Models;
using SOBE.Services;

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
        private readonly IQueueService _queue;

        public DownloadController(IWebHostEnvironment env, IQueueService queue)
        {
            _env = env;
            _storagePath = Path.GetTempPath();
            _queue = queue;
        }

        /// <summary>
        /// Creates a new download request
        /// </summary>
        /// <response code="200">If request was successfully submitted. Includes request ID.</response>
        [HttpPost]
        [Produces("application/json")]
        public ActionResult<RequestHandle> Post(DownloadRequest downloadRequest)
        {
            var outputName = downloadRequest.OutputName ?? Path.GetFileName(downloadRequest.FileUrl);
            var requestId = Guid.NewGuid();
            var downloadDir = System.IO.Directory.CreateDirectory(AbsPath(requestId.ToString()));
            var result = new RequestHandle { Id = requestId, Message = "File successfully submitted for processing" };
            _queue.SendMessage(new DownloadRequestMessage() {FileUrl = downloadRequest.FileUrl, FileName = outputName, RequestId = requestId.ToString() });
            // Task.Run(async () => await ProcessFile(downloadRequest.FileUrl, downloadDir.FullName, outputName)).ContinueWith(async (r) =>
            // {
            //     if (r.IsCompletedSuccessfully)
            //         Console.WriteLine($"[ProcessFile] [{outputName}] Success");
            //     else
            //     {
            //         var errorMessage = new StringBuilder("Could not process the file.");
            //         foreach (var exception in r.Exception.InnerExceptions)
            //         {
            //             Console.Error.WriteLine($"Exception in [ProcessFile] [{outputName}]: {exception.Message}{Environment.NewLine} Stack Trace: {exception.StackTrace}");
            //             errorMessage.Append($" Error: [{exception.Message}]");
            //         }
            //         await System.IO.File.WriteAllTextAsync(Path.Combine(downloadDir.FullName, "work.error"), errorMessage.ToString());
            //     }
            // });
            return Ok(result);
        }

        /// <summary>
        /// Retrieves the status for a previously created download request
        /// </summary>
        /// <response code="200">Returns the status of the request</response>
        /// <response code="400">If no requestId was specified</response>
        /// <response code="404">If no request was found for the specified requestId</response>  
        [HttpGet, Route("status")]
        [Produces("application/json")]
        public async Task<ActionResult<RequestHandle>> GetStatus([FromQuery]string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return BadRequest("No requestId was specified");
            var dirPath = AbsPath(requestId);
            var successFlag = Path.Combine(dirPath, "work.done");
            var errorFlag = Path.Combine(dirPath, "work.error");
            if (System.IO.File.Exists(successFlag))
                return Ok(new RequestHandle { Id = new Guid(requestId), Finished = true, ReadyForDownload = true, Message = "File processed succesfully" });
            else if (System.IO.File.Exists(errorFlag))
                return Ok(new RequestHandle { Id = new Guid(requestId), Finished = true, Message = await System.IO.File.ReadAllTextAsync(errorFlag) });
            else if (System.IO.Directory.Exists(dirPath))
                return Ok(new RequestHandle { Id = new Guid(requestId) });
            else
                return NotFound();
        }

        /// <summary>
        /// Downloads the result of a request, if the request was succesful
        /// </summary>
        /// <response code="200">Returns the file for download</response>
        /// <response code="400">If no requestId was specified</response>
        /// <response code="404">If no finished request was found for the specified requestId</response>  
        [HttpGet]
        public async Task<ActionResult> Get([FromQuery]string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return BadRequest("No requestId was specified");
            var dirPath = AbsPath(requestId);
            var flag = Path.Combine(dirPath, "work.done");
            if (System.IO.File.Exists(flag))
            {
                var path = await System.IO.File.ReadAllTextAsync(flag);
                var fileName = Path.GetFileName(path);

                var stream = System.IO.File.OpenRead(path);
                return File(stream, "application/octet-stream", fileName);
            }
            else
            {
                return NotFound();
            }
        }



        public class RequestHandle
        {
            public Guid Id { get; set; }
            public bool Finished { get; set; }
            public bool ReadyForDownload { get; set; }
            public string Message { get; set; }
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

        private string AbsPath(string fileName) => Path.Combine(_storagePath, fileName);
    }
}