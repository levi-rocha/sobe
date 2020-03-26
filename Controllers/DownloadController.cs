using System;
using System.IO;
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

        public DownloadController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet]
        public ActionResult<RequestHandle> Get([FromQuery]string requestId)
        {
            if (System.IO.File.Exists(requestId))
                return Ok(new RequestHandle { Id = new Guid(requestId) });
            else if (System.IO.File.Exists($"{requestId}_done"))
                return Ok(new RequestHandle { Id = new Guid(requestId), Ready = true, Path = System.IO.File.ReadAllText($"{requestId}_done")});
            else
                return NotFound();
        }

        [HttpPost]
        public async Task<ActionResult<RequestHandle>> Post(DownloadRequest downloadRequest)
        {
            var downloadedName = downloadRequest.OutputName ?? Path.GetFileName(downloadRequest.FileUrl);
            var downloadedPath = downloadedName;

            var requestId = Guid.NewGuid();

            var result = new RequestHandle { Id = requestId, Ready = false };

            await System.IO.File.WriteAllTextAsync(requestId.ToString(), string.Empty);

            Task.Run(async () => await ProcessFile(downloadRequest.FileUrl, downloadedPath, requestId));

            return Ok(result);
        }

        public class RequestHandle
        {
            public Guid Id { get; set; }
            public bool Ready { get; set; }
            public string Path { get; set; }
        }

        private async Task ProcessFile(string fileUrl, string downloadedPath, Guid requestId)
        {
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
            System.IO.File.Delete(requestId.ToString());
            await System.IO.File.WriteAllTextAsync($"{requestId}_done", downloadedPath);
        }
    }
}