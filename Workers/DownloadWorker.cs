using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOBE.Services;

public class DownloadWorker : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _services;
    private IQueueService _queueService;
    private IDownloadService _downloadService;

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
            _downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
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
            var sha1 = await ProcessRequest(msg);
            msg.Sha1 = sha1;
            ForwardMessage(msg);
            _logger.LogDebug("Download worker run finished");
        }
        else
        {
            _logger.LogDebug("No download message received");
            Thread.Sleep(TimeSpan.FromSeconds(20)); //todo: extract and change
        }
    }

    public async Task<string> ProcessRequest(DownloadRequestMessage request)
    {
        _logger.LogInformation($"Downloading request {request.RequestId}");
        var sha1 = await _downloadService.DownloadAsync(request.FileUrl, request.FileName, request.RequestId);
        _logger.LogInformation($"Finished download for request {request.RequestId}");
        return sha1;
    }

    public void ForwardMessage(DownloadRequestMessage message)
    {
        _logger.LogDebug($"Forwarding scan request for {message.RequestId}");
        var nextMessage = new ScanRequestMessage()
        {
            RequestId = message.RequestId,
            Sha1 = message.Sha1,
            FilePath = Path.Combine(message.RequestId, message.FileName)
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Forwarded scan request for {message.RequestId}");
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Download worker is stopping.");

        await Task.CompletedTask;
    }
}