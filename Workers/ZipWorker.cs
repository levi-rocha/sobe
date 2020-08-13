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
    public class ZipWorker : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _services;
        private IQueueService _queueService;
        private IStorageService _storageService;
        private IZipService _zipService;

        public ZipWorker(IServiceProvider services, ILogger<ZipWorker> logger)
        {
            _logger = logger;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stopToken)
        {
            _logger.LogInformation("Zip Worker starting");
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
                _zipService = scope.ServiceProvider.GetRequiredService<IZipService>();
                while (!stopToken.IsCancellationRequested && !applicationStoppingToken.IsCancellationRequested)
                {
                    await DoWorkAsync();
                }
            }
        }

        public async Task DoWorkAsync()
        {
            _logger.LogDebug("Zip worker run started");
            var msg = _queueService.ReceiveZipRequest();
            if (msg != null)
            {
                try
                {
                    var zipPath = $"{msg.Sha1}.zip";
                    using (var fileStream = await _storageService.GetAsStreamAsync(msg.FilePath))
                    {
                        using (var zipStream = await _zipService.Zip(fileStream, msg.FileName))
                        {
                            await _storageService.WriteAsync(zipStream, zipPath);
                        }
                    }
                    await _storageService.DeleteAsync(msg.RequestId);
                    ForwardMessage(msg, zipPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing request {msg.RequestId}: {ex.Message} | StackTrace: {ex.StackTrace.ToString()}");
                    ForwardError(msg, ex.Message);
                }
                _logger.LogDebug("Zip worker run finished");
            }
            else
            {
                _logger.LogDebug("No Zip message received");
                Thread.Sleep(TimeSpan.FromSeconds(25)); //todo: extract and change
            }
        }

        public void ForwardMessage(ZipRequestMessage message, string zipPath)
        {
            _logger.LogDebug($"Registering finished request for {message.RequestId}");
            var nextMessage = new FinishedRequestMessage()
            {
                RequestId = message.RequestId,
                Sha1 = message.Sha1,
                FileName = Path.GetFileName(zipPath),
                FilePath = zipPath,
                RequestResult = RequestResult.ReadyForDownload,
                Message = "File processed successfuly",
                Owner = message.Owner
            };
            _queueService.SendMessage(nextMessage);
            _logger.LogDebug($"Registered finished request for {message.RequestId}");
        }

        public void ForwardError(ZipRequestMessage message, string errorMessage)
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
                "Zip worker is stopping.");

            await Task.CompletedTask;
        }
    }
}