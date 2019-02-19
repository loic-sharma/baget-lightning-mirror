using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Mirror;
using BaGet.Core.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Willezone.Azure.WebJobs.Extensions.DependencyInjection;

namespace BaGet
{
    // Increase lock duration to 2 mintues
    // Limit to 80 workers
    // Database to 400 DTUs (S6)
    public static class IndexFunction
    {
        [FunctionName("IndexFunction")]
        public static async Task Run(
            [ServiceBusTrigger("index", Connection = "ServiceBusConnectionString")]
            byte[] packageUrl,
            [Inject]IPackageDownloader downloader,
            [Inject]IPackageIndexingService indexer,
            ILogger log,
            CancellationToken cancellationToken)
        {
            var packageUri = new Uri(Encoding.Unicode.GetString(packageUrl));

            using (var packageStream = await downloader.DownloadOrNullAsync(packageUri, cancellationToken))
            {
                if (packageStream == null)
                {
                    log.LogError("Could not find package at url {PackageUrl}", packageUri);
                    return;
                }

                await indexer.IndexAsync(packageStream, cancellationToken);
            }
        }
    }
}
