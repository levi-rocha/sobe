using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOBE.Services;

public class DownloadWorker : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IQueueService _queueService;
    private readonly IDownloadService _downloadService;

    public DownloadWorker(ILogger logger, IQueueService queueService, IDownloadService downloadService)
    {
        _logger = logger;
        _queueService = queueService;
        _downloadService = downloadService;
    }

    protected override async Task ExecuteAsync(CancellationToken stopToken)
    {
        _logger.LogInformation("Download Worker started");
        while (!stopToken.IsCancellationRequested)
        {
            await DoWorkAsync();
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
        }
        else { _logger.LogDebug("No download message received"); }
        _logger.LogDebug("Download worker run finished");
        Thread.Sleep(5000); //todo: extract and change time
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
            FilePath = message.FileName
        };
        _queueService.SendMessage(nextMessage);
        _logger.LogDebug($"Forwarded scan request for {message.RequestId}");
    }
}