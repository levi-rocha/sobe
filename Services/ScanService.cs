using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SOBE.Options;

public interface IScanService
{
    Task<bool> IsSafe(string sha1);
    Task RequestScanAsync(Stream contentStream);
}

public class VirusTotalScanService : IScanService
{
    private const string ENDPOINT_VIRUSTOTAL_REPORT = "https://www.virustotal.com/vtapi/v2/file/report";
    private const string ENDPOINT_VIRUSTOTAL_SCAN = "https://www.virustotal.com/vtapi/v2/file/scan";

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly ScanOptions _scanOptions;

    public VirusTotalScanService(HttpClient httpClient, ILogger<VirusTotalScanService> logger, IOptions<ScanOptions> scanOptions)
    {
        _logger = logger;
        _httpClient = httpClient;
        _scanOptions = scanOptions.Value;
    }

    public Task<bool> IsSafe(string sha1)
    {
        var urlReport = $"{ENDPOINT_VIRUSTOTAL_REPORT}?resource={sha1}&apikey={_scanOptions.VirusTotalApiKey}";
        var response = JsonConvert.DeserializeObject<dynamic>(_httpClient.GetAsync(urlReport).Result.Content.ReadAsStringAsync().Result);
        _logger.LogDebug($"response virustotal: response code: {response.response_code} | positives: {response.positives}");
        if (response.response_code == 1)
            return Task.FromResult(response.positives == 0);
        else
            throw new VirusTotalNotFoundException($"VirusTotal error! Response code: {response.response_code} | Message: {response.verbose_msg}");
    }

    public async Task RequestScanAsync(Stream contentStream)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(_scanOptions.VirusTotalApiKey), "apikey");
        content.Add(new StreamContent(contentStream), "file");
        var response = await _httpClient.PostAsync(ENDPOINT_VIRUSTOTAL_SCAN, content);
        if (!response.IsSuccessStatusCode)
        {
            var msg = $"Could not upload to VirusTotal: {response.Content.ReadAsStringAsync().Result}";
            _logger.LogError(msg);
            throw new Exception(msg);
        }
    }
}

public class VirusTotalNotFoundException : Exception
{
    public VirusTotalNotFoundException(string msg) : base(msg) { }
}