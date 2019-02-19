extern alias AzureStorageBlob;
extern alias AzureStorageCommon;

using AzureStorageCommon.Microsoft.WindowsAzure.Storage;
using AzureStorageCommon.Microsoft.WindowsAzure.Storage.Auth;
using AzureStorageBlob.Microsoft.WindowsAzure.Storage.Blob;

using System;
using System.Net;
using System.Net.Http;
using BaGet;
using BaGet.Core.Entities;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Willezone.Azure.WebJobs.Extensions.DependencyInjection;
using BaGet.Azure.Configuration;

[assembly: WebJobsStartup(typeof(Startup))]
namespace BaGet
{
    public class Startup : IWebJobsStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
            builder.AddDependencyInjection(ConfigureServices);
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IPackageDownloader, PackageDownloader>();
            services.AddTransient<IPackageIndexingService, PackageIndexingService>();
            services.AddTransient<IPackageService, PackageService>();
            services.AddSingleton<IPackageStorageService, PackageStorageService>();
            services.AddTransient<ISearchService, DatabaseSearchService>();
            services.AddTransient<IStorageService, BlobStorageService>();
            services.AddScoped<IContext, SqlServerContext>();

            services.AddSingleton(provider =>
            {
                var client = new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                });

                client.DefaultRequestHeaders.Add("User-Agent", "BaGet/0.1.54-prerelease");
                client.Timeout = TimeSpan.FromSeconds(300);

                return client;
            });

            services.AddDbContext<SqlServerContext>((provider, options) =>
            {
                var connectionString = GetConfig("DatabaseConnectionString");

                options.UseSqlServer(connectionString);
            });

            services.AddSingleton(provider =>
            {
                var accountName = GetConfig("AzureBlobStorageAccountName");
                var accessKey = GetConfig("AzureBlobStorageAccessKey");

                return new CloudStorageAccount(
                    new StorageCredentials(
                        accountName,
                        accessKey),
                    useHttps: true);
            });

            services.AddTransient(provider =>
            {
                var account = provider.GetRequiredService<CloudStorageAccount>();
                var container = GetConfig("AzureBlobStorageContainer");

                var client = account.CreateCloudBlobClient();

                return client.GetContainerReference(container);
            });
        }

        private string GetConfig(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }
}
