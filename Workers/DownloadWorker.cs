using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOBE.Services;

namespace SOBE.Workers
{
    public class DownloadWorker : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _services;
        private IQueueService _queueService;
        private IStorageService _storageService;

        public DownloadWorker(IServiceProvider services, ILogger<DownloadWorker> logger)
        {
            _logger = logger;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stopToken)
        {
            _logger.LogInformation("Download Worker starting");
            await StartWorkerAsync(stopToken);
        }

        private async Task StartWorkerAsync(CancellationToken stopToken)
        {
            await Task.Yield();
            var applicationStoppingToken = _services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            using (var scope = _services.CreateScope())
            {
                _queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
                _storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
                while (!stopToken.IsCancellationRequested && !applicationStoppingToken.IsCancellationRequested)
                {
                    await DoWorkAsync();
                }
            }
        }

        public async Task DoWorkAsync()
        {
            _logger.LogDebug("Download worker run started");
            var msg = _queueService.ReceiveDownloadRequest();
            if (msg != null)
            {
                try
                {
                    msg = await ProcessRequest(msg);
                    _logger.LogDebug($"SHA1 for request {msg.RequestId} is {msg.Sha1}");
                    var zipPath = $"{msg.Sha1}.zip";
                    if (_storageService.Exists(zipPath))
                    {
                        ForwardAlreadyExistsMessage(msg, zipPath);
                        await _storageService.DeleteAsync(msg.RequestId);
                    }
                    else
                        ForwardMessage(msg);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing request {msg.RequestId}: {ex.Message} | StackTrace: {ex.StackTrace.ToString()}");
                    ForwardError(msg, ex.Message);
                }
                _logger.LogDebug("Download worker run finished");
            }
            else
            {
                _logger.LogDebug("No download message received");
                Thread.Sleep(TimeSpan.FromSeconds(20)); //todo: extract and change
            }
        }

        private void ForwardAlreadyExistsMessage(DownloadRequestMessage message, string zipPath)
        {
            _logger.LogDebug($"Registering request {message.RequestId} as finished: Already exists");
            var nextMessage = new FinishedRequestMessage()
            {
                RequestId = message.RequestId,
                Sha1 = message.Sha1,
                FilePath = zipPath,
                RequestResult = RequestResult.ReadyForDownload,
                Owner = message.Owner
            };
            _queueService.SendMessage(nextMessage);
            _logger.LogDebug($"Registered request {message.RequestId} as finished");
        }

        public async Task<DownloadRequestMessage> ProcessRequest(DownloadRequestMessage request)
        {
            _logger.LogInformation($"Downloading request {request.RequestId}");
            var downloadResult = await _storageService.DownloadFromUrlAsync(request.FileUrl, request.RequestId);
            _logger.LogInformation($"Finished download for request {request.RequestId}");
            request.Sha1 = downloadResult.Sha1;
            request.FileName = request.FileName ?? downloadResult.FileName;
            return request;
        }

        public void ForwardMessage(DownloadRequestMessage message)
        {
            _logger.LogDebug($"Forwarding scan request for {message.RequestId}");
            var nextMessage = new ScanRequestMessage()
            {
                RequestId = message.RequestId,
                Sha1 = message.Sha1,
                FileName = message.FileName,
                FilePath = Path.Combine(message.RequestId, message.FileName),
                Owner = message.Owner,
            };
            _queueService.SendMessage(nextMessage);
            _logger.LogDebug($"Forwarded scan request for {message.RequestId}");
        }

        public void ForwardError(DownloadRequestMessage message, string errorMessage)
        {
            _logger.LogDebug($"Registering error for {message.RequestId}. Error message: {errorMessage}");
            var nextMessage = new FinishedRequestMessage()
            {
                RequestId = message.RequestId,
                Sha1 = message.Sha1,
                RequestResult = RequestResult.Error,
                Message = errorMessage,
                Owner = message.Owner
            };
            _queueService.SendMessage(nextMessage);
            _logger.LogDebug($"Registered error for {message.RequestId}");
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Download worker is stopping.");

            await Task.CompletedTask;
        }
    }
}
