using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using SOBE.Models;

namespace SOBE.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
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

            Task.Run(async () => await ProcessFile(downloadRequest.FileUrl, downloadDir.FullName, outputName)).ContinueWith((r) => {
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

            // Zip
            ZipFile.CreateFromDirectory(Path.Combine(downloadDir, "src"), zippedPath);

            // Cleanup
            System.IO.Directory.Delete(srcPath, recursive: true);
            await System.IO.File.WriteAllTextAsync(Path.Combine(downloadDir, "work.done"), zippedPath);
        }

        public string AbsPath(string fileName) => Path.Combine(_storagePath, fileName);
    }
}