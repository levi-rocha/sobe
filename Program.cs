using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOBE.Services;
using SOBE.Workers;

namespace SOBE
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                }).ConfigureServices(services =>
                {
                    services.AddSingleton<IQueueService, MemoryQueueService>();
                    services.AddHttpClient<IScanService, VirusTotalScanService>();
                    services.AddHttpClient<IStorageService, LocalStorageService>();
                    services.AddScoped<IZipService, LocalZipService>();

                    services.AddHostedService<DownloadWorker>();
                    services.AddHostedService<ScanWorker>();
                    services.AddHostedService<ZipWorker>();
                });
    }
}
