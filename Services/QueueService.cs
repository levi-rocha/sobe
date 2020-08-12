using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SOBE.Services
{
    public abstract class RequestMessage
    {
        public string RequestId { get; set; }
        public string Sha1 { get; set; }
        public string FileName { get; set; }
        public string Owner { get; set; }
    }

    public class DownloadRequestMessage : RequestMessage
    {
        public string FileUrl { get; set; }
    }

    public class RequestMessageWithFilePath : RequestMessage
    {
        public string FilePath { get; set; }
    }

    public class ScanRequestMessage : RequestMessageWithFilePath { }
    public class ZipRequestMessage : RequestMessageWithFilePath { }
    public class FinishedRequestMessage : RequestMessageWithFilePath
    {
        public string Message { get; set; }
        public RequestResult RequestResult { get; set; }
    }

    public enum RequestResult
    {
        None,
        ReadyForDownload,
        Error
    }

    public interface IQueueService
    {
        void SendMessage(DownloadRequestMessage requestMessage);
        void SendMessage(ScanRequestMessage requestMessage);
        void SendMessage(ZipRequestMessage requestMessage);
        void SendMessage(FinishedRequestMessage requestMessage);
        DownloadRequestMessage ReceiveDownloadRequest();
        ScanRequestMessage ReceiveScanRequest();
        ZipRequestMessage ReceiveZipRequest();
        FinishedRequestMessage GetResult(string requestId);
    }

    public class MemoryQueueService : IQueueService
    {
        private ConcurrentQueue<DownloadRequestMessage> _downloadRequests = new ConcurrentQueue<DownloadRequestMessage>();
        private ConcurrentQueue<ScanRequestMessage> _scanRequests = new ConcurrentQueue<ScanRequestMessage>();
        private ConcurrentQueue<ZipRequestMessage> _zipRequests = new ConcurrentQueue<ZipRequestMessage>();
        private ConcurrentQueue<FinishedRequestMessage> _finishedRequests = new ConcurrentQueue<FinishedRequestMessage>();
        private readonly ILogger _logger;

        public MemoryQueueService(ILogger<MemoryQueueService> logger)
        {
            _logger = logger;
        }

        public DownloadRequestMessage ReceiveDownloadRequest()
        {
            DownloadRequestMessage message;
            if (_downloadRequests.TryDequeue(out message))
            {
                _logger.LogInformation($"Message {message.RequestId} dequeued from DOWNLOAD queue");
                return message;
            }
            else
            {
                return null;
            }
        }

        public ScanRequestMessage ReceiveScanRequest()
        {
            ScanRequestMessage message;
            if (_scanRequests.TryDequeue(out message))
            {
                _logger.LogInformation($"Message {message.RequestId} dequeued from SCAN queue");
                return message;
            }
            else
            {
                return null;
            }
        }

        public ZipRequestMessage ReceiveZipRequest()
        {
            ZipRequestMessage message;
            if (_zipRequests.TryDequeue(out message))
            {
                _logger.LogInformation($"Message {message.RequestId} dequeued from ZIP queue");
                return message;
            }
            else
            {
                return null;
            }
        }

        public FinishedRequestMessage GetResult(string requestId)
            => _finishedRequests.FirstOrDefault(m => m.RequestId == requestId);

        public void SendMessage(FinishedRequestMessage requestMessage)
        {
            _finishedRequests.Enqueue(requestMessage);
            _logger.LogInformation($"Message {requestMessage.RequestId} queued as FINISHED - Result: {requestMessage.RequestResult.ToString()}");
        }

        public void SendMessage(DownloadRequestMessage requestMessage)
        {
            _downloadRequests.Enqueue(requestMessage);
            _logger.LogInformation($"Message {requestMessage.RequestId} queued for DOWNLOAD");
        }

        public void SendMessage(ScanRequestMessage requestMessage)
        {
            _scanRequests.Enqueue(requestMessage);
            _logger.LogInformation($"Message {requestMessage.RequestId} queued for SCAN");
        }

        public void SendMessage(ZipRequestMessage requestMessage)
        {
            _zipRequests.Enqueue(requestMessage);
            _logger.LogInformation($"Message {requestMessage.RequestId} queued for ZIP");
        }
    }
}