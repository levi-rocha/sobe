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
    private IStorageService _storageService;
    private IScanService _scanService;

    public ScanWorker(IServiceProvider services, ILogger<ScanWorker> logger)
    {
        _logger = logger;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _logger.LogInformation("Scan Worker starting");
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
        var msg = _queueService.ReceiveScanRequest();
        if (msg != null)
        {
            bool isSafe = false;
            var scanned = TryScan(msg.Sha1, out isSafe);
            if (scanned)
            {
                _logger.LogInformation($"Scan finished for request {msg.RequestId}");
                if (isSafe)
                    ForwardMessage(msg);
                else
                    ForwardError(msg, "Threat detected by Security scan");
            }
            else
            {
                _logger.LogInformation($"No previous security scan report found for request {msg.RequestId}");
                await RequestScanAsync(msg);
                RequeueMessage(msg);
            }

            _logger.LogDebug("Scan worker run finished");
        }
        else
        {
            _logger.LogDebug("No scan message received");
        }
        Thread.Sleep(TimeSpan.FromSeconds(30)); //todo: extract and change
    }

    public bool TryScan(string sha1, out bool isSafe)
    {
        isSafe = false;
        try
        {
            isSafe = _scanService.IsSafe(sha1).Result;
            _logger.LogDebug($"isSafe set to {isSafe.ToString()}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error while checking for scan report {ex.Message}");
            return false;
        }
    }

    public async Task RequestScanAsync(ScanRequestMessage msg)
    {
        _logger.LogInformation($"Requesting security scan for request {msg.RequestId}");
        try
        {
            var contentStream = await _storageService.GetAsStreamAsync(msg.FilePath);
            await _scanService.RequestScanAsync(contentStream);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during scan request: {ex.Message} | StackTrace: {ex.StackTrace.ToString()}");
        }
    }

    public void ForwardMessage(ScanRequestMessage message)
    {
        _logger.LogDebug($"Forwarding zip request for {message.RequestId}");
        var nextMessage = new ZipRequestMessage()
        {
            RequestId = message.RequestId,
            Sha1 = message.Sha1,
            FileName = message.FileName,
            FilePath = message.FilePath
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Forwarded zip request for {message.RequestId}");
    }

    public void RequeueMessage(ScanRequestMessage message)
    {
        _logger.LogDebug($"Requeueing scan request for {message.RequestId}");
        var nextMessage = new ScanRequestMessage()
        {
            RequestId = message.RequestId,
            Sha1 = message.Sha1,
            FileName = message.FileName,
            FilePath = message.FilePath
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Requeueing scan request for {message.RequestId}");
    }

    public void ForwardError(ScanRequestMessage message, string errorMessage)
    {
        _logger.LogDebug($"Registering error for {message.RequestId}. Error message: {errorMessage}");
        var nextMessage = new FinishedRequestMessage()
        {
            RequestId = message.RequestId,
            Sha1 = message.Sha1,
            RequestResult = RequestResult.Error,
            Message = errorMessage
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Registered error for {message.RequestId}");
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Scan worker is stopping.");

        await Task.CompletedTask;
    }
}