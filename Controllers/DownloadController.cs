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
        //todo: MAJ doc
        private readonly IQueueService _queue;
        private readonly IStorageService _storageService;

        public DownloadController(IWebHostEnvironment env, IQueueService queue, IStorageService storageService)
        {
            _queue = queue;
            _storageService = storageService;
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
            var result = new RequestHandle { Id = requestId, Message = "File successfully submitted for processing" };
            _queue.SendMessage(new DownloadRequestMessage() { FileUrl = downloadRequest.FileUrl, FileName = outputName, RequestId = requestId.ToString() });
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
        public ActionResult<RequestHandle> GetStatus([FromQuery]string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return BadRequest("No requestId was specified");
            var finishedMsg = _queue.GetResult(requestId);
            if (finishedMsg == null)
                return NotFound($"No finished request found for Id {requestId}");
            switch (finishedMsg.RequestResult)
            {
                case RequestResult.ReadyForDownload:
                    return Ok(new RequestHandle { Id = new Guid(requestId), Finished = true, ReadyForDownload = true, Message = "File processed succesfully" });
                case RequestResult.Error:
                    return Ok(new RequestHandle { Id = new Guid(requestId), Finished = true, Message = finishedMsg.Message });
                default:
                    return Ok(new RequestHandle { Id = new Guid(requestId), Finished = true, Message = finishedMsg.Message });
            }
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

            var finishedMsg = _queue.GetResult(requestId);

            if (finishedMsg?.RequestResult == RequestResult.ReadyForDownload)
            {
                var stream = await _storageService.GetAsStreamAsync(finishedMsg.FilePath);
                return File(stream, "application/octet-stream", finishedMsg.FileName);
            }
            return NotFound($"No request ready to download found for Id {requestId}");
        }

        public class RequestHandle
        {
            public Guid Id { get; set; }
            public bool Finished { get; set; }
            public bool ReadyForDownload { get; set; }
            public string Message { get; set; }
        }
    }
}