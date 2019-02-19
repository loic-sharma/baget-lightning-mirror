using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using Microsoft.Azure.ServiceBus;
using MoreLinq.Extensions;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet
{
    public class Program
    {
        private const int MaxDegreeOfParallelism = 64;

        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task MainAsync(string[] args)
        {
            // Parse CLI arguments.
            if (args.Length != 2)
            {
                Console.WriteLine("Please provide a service index url and a service bus connection string");
                return;
            }

            var sourceUrl = args[0];
            var connectionString = args[1];

            // Prepare the processing.
            ThreadPool.SetMinThreads(MaxDegreeOfParallelism, completionPortThreads: 4);
            ServicePointManager.DefaultConnectionLimit = MaxDegreeOfParallelism;
            ServicePointManager.MaxServicePointIdleTime = 10000;

            var httpClient = new HttpClient();

            var builder = new ServiceBusConnectionStringBuilder(connectionString);
            var queue = new QueueClient(builder, ReceiveMode.PeekLock);

            // Discover the catalog index URL from the service index.
            var catalogIndexUrl = await GetCatalogIndexUrlAsync(httpClient, sourceUrl);

            // Download the catalog index and deserialize it.
            Console.WriteLine($"Fetching catalog index {catalogIndexUrl}...");
            var indexString = await httpClient.GetStringAsync(catalogIndexUrl);
            Console.WriteLine($"Fetched catalog index {catalogIndexUrl}, fetching catalog pages...");
            var index = JsonConvert.DeserializeObject<CatalogIndex>(indexString);

            // Find all pages in the catalog index.
            var pageItems = new ConcurrentBag<CatalogPage>(index.Items);
            var allLeafItemsBag = new ConcurrentBag<CatalogLeaf>();

            await ProcessInParallel(pageItems, async pageItem =>
            {
                // Download the catalog page and deserialize it.
                var pageString = await httpClient.GetStringAsync(pageItem.Url);
                var page = JsonConvert.DeserializeObject<CatalogPage>(pageString);

                var pageLeafItems = page.Items;

                foreach (var pageLeafItem in page.Items)
                {
                    allLeafItemsBag.Add(pageLeafItem);
                }
            });

            Console.WriteLine($"Fetched {index.Items.Count} catalog pages, finding catalog leaves...");

            var filteredLeafItems = allLeafItemsBag
                .GroupBy(l => new PackageIdentity(l.Id, NuGetVersion.Parse(l.Version)))
                .Select(g => g.OrderByDescending(l => l.CommitTimeStamp).First())
                .Where(l => l.Type == "nuget:PackageDetails")
                .ToList();

            // Process all of the catalog leaf items.
            Console.WriteLine($"Processing {filteredLeafItems.Count} packages...");

            var leafItems = new ConcurrentBag<CatalogLeaf>(filteredLeafItems);

            await ProcessInParallel(leafItems, async leafItem =>
            {
                var id = leafItem.Id.ToLowerInvariant();
                var version = NuGetVersion.Parse(leafItem.Version).ToNormalizedString().ToLowerInvariant();
                var packageUrl = $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg";

                await queue.SendAsync(new Message
                {
                    Body = Encoding.Unicode.GetBytes(packageUrl),
                    ContentType = "text/plain;charset=unicode"
                });
            });

            Console.WriteLine();
            Console.WriteLine("Done");
        }

        private static async Task<Uri> GetCatalogIndexUrlAsync(HttpClient httpClient, string sourceUrl)
        {
            var client = new ServiceIndexClient(httpClient);

            var serviceIndex = await client.GetServiceIndexAsync(sourceUrl);
            var resource = serviceIndex.Resources.First(r => r.Type == "Catalog/3.0.0");

            return new Uri(resource.Url);
        }

        private static async Task ProcessInParallel<T>(ConcurrentBag<T> items, Func<T, Task> work)
        {
            var tasks = Enumerable
                .Range(0, MaxDegreeOfParallelism)
                .Select(async i =>
                {
                    while (items.TryTake(out var item))
                    {
                        await work(item);
                    }
                })
                .ToList();

            tasks.Add(PrintProcess(items));

            await Task.WhenAll(tasks);
        }

        private static async Task PrintProcess<T>(ConcurrentBag<T> items)
        {
            while (items.Count > 0)
            {
                Console.WriteLine($"{items.Count} items remaining...");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }
}
