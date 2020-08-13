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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SOBE.Models;
using SOBE.Options;
using SOBE.Services;

namespace SOBE.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
        //todo: MAJ doc

        private static readonly string PASSWORD_ENCRYPTED = "460883b602ce0dd1dc6f6202378d394a00ddc9929cf4b961e5acd75e1479c802de7dd7f429867ece25235c2c8406d7057ccfc66ec9be68948f5c03db130c8d0b"; //Read from config

        private readonly IQueueService _queue;
        private readonly IStorageService _storageService;
        private readonly ILogger _logger;
        private AuthOptions _authOptions;

        public DownloadController(IWebHostEnvironment env, IQueueService queue, IStorageService storageService, ILogger<DownloadController> logger, IOptions<AuthOptions> authOptions)
        {
            _queue = queue;
            _storageService = storageService;
            _logger = logger;
            _authOptions = authOptions.Value;
        }

        /// <summary>
        /// Creates a new download request
        /// </summary>
        /// <response code="200">If request was successfully submitted. Includes request ID.</response>
        [HttpPost]
        [Produces("application/json")]
        public ActionResult<RequestHandle> Post(DownloadRequest downloadRequest)
        {
            var password = GetSHA512(downloadRequest.Password).ToLower();
            if (password != _authOptions.EncryptedPassword.ToLower())
            {
                _logger.LogWarning("Denied request for {fileUrl} from {owner} at {ip} with Bad Password", downloadRequest.FileUrl, downloadRequest.Owner, Request.HttpContext.Connection.RemoteIpAddress);
                return Unauthorized();
            }
            var requestId = Guid.NewGuid();
            _logger.LogInformation("Approved request {requestId} for {fileUrl} from {owner} at {ip}", requestId, downloadRequest.FileUrl, downloadRequest.Owner, Request.HttpContext.Connection.RemoteIpAddress);
            var result = new RequestHandle { Id = requestId, Message = "File successfully submitted for processing" };
            _queue.SendMessage(new DownloadRequestMessage() { FileUrl = downloadRequest.FileUrl, FileName = downloadRequest.OutputName, RequestId = requestId.ToString(), Owner = downloadRequest.Owner });
            return Ok(result);
        }

        public static string GetSHA512(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            using (SHA512 hash = new SHA512Managed())
            {
                var hashedInputBytes = hash.ComputeHash(bytes);
                var hashedInputStringBuilder = new System.Text.StringBuilder(128);
                foreach (var b in hashedInputBytes)
                    hashedInputStringBuilder.Append(b.ToString("X2"));
                return hashedInputStringBuilder.ToString();
            }
        }

        /// <summary>
        /// Retrieves the status for a previously created download request
        /// </summary>
        /// <response code="200">Returns the status of the request</response>
        /// <response code="400">If no requestId was specified</response>
        /// <response code="404">If no request was found for the specified requestId</response>  
        [HttpGet, Route("status")]
        [Produces("application/json")]
        public ActionResult<RequestHandle> GetStatus([FromQuery] string requestId)
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
        public async Task<ActionResult> Get([FromQuery] string requestId)
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