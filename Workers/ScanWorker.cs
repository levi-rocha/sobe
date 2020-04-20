using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOBE.Services;

public class ScanWorker : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _services;
    private IQueueService _queueService;
    private IDownloadService _downloadService;
    private IScanService _scanService;

    public ScanWorker(IServiceProvider services, ILogger<DownloadWorker> logger)
    {
        _logger = logger;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _logger.LogInformation("Scan Worker starting");
        var applicationStoppingToken = _services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        using (var scope = _services.CreateScope())
        {
            _queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
            _downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            _scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            while (!stopToken.IsCancellationRequested && !applicationStoppingToken.IsCancellationRequested)
            {
                await DoWorkAsync();
            }
        }
    }

    public async Task DoWorkAsync()
    {
        _logger.LogDebug("Scan worker run started");
        var msg = _queueService.ReceiveDownloadRequest();
        if (msg != null)
        {
            bool isSafe = false;
            var scanned = TryScan(msg.Sha1, out isSafe);
            if (scanned)
            {
                //todo: check isSafe and forward zip if true, forward error if false
                ForwardMessage(msg);
            }
            else
                RequestScan(msg);
            
            _logger.LogDebug("Scan worker run finished");
        }
        else
        {
            _logger.LogDebug("No scan message received");
            Thread.Sleep(TimeSpan.FromSeconds(30)); //todo: extract and change
        }
    }

    public bool TryScan(string sha1, out bool isSafe)
    {
        //todo: try catch scanService.IsSafe
        isSafe = false;
        return false;
    }

    public void RequestScan(ScanRequestMessage msg)
    {
        //todo: download and scanService.RequestScans
    }

    //todo: replace with forward options (zip, error)
    public void ForwardMessage(DownloadRequestMessage message)
    {
        _logger.LogDebug($"Forwarding scan request for {message.RequestId}");
        var nextMessage = new ScanRequestMessage()
        {
            RequestId = message.RequestId,
            Sha1 = message.Sha1,
            FilePath = message.FileName
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Forwarded scan request for {message.RequestId}");
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Scan worker is stopping.");

        await Task.CompletedTask;
    }
}