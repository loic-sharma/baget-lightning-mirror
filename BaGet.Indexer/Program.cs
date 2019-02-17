using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet.Indexer
{
    class Program
    {
        private const int MaxDegreeOfParallelism = 64;

        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task MainAsync(string[] args)
        {
            // Parse CLI arguments.
            if (args.Length != 1)
            {
                Console.WriteLine("Please provide a service index url");
                return;
            }

            var sourceUrl = args[0];

            // Prepare the processing.
            ThreadPool.SetMinThreads(MaxDegreeOfParallelism, completionPortThreads: 4);
            ServicePointManager.DefaultConnectionLimit = MaxDegreeOfParallelism;
            ServicePointManager.MaxServicePointIdleTime = 10000;

            var httpClient = new HttpClient();

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

            var fetchLeafsTasks = RunInParallel(async () =>
            {
                while (pageItems.TryTake(out var pageItem))
                {
                    // Download the catalog page and deserialize it.
                    var pageString = await httpClient.GetStringAsync(pageItem.Url);
                    var page = JsonConvert.DeserializeObject<CatalogPage>(pageString);

                    var pageLeafItems = page.Items;

                    foreach (var pageLeafItem in page.Items)
                    {
                        allLeafItemsBag.Add(pageLeafItem);
                    }
                }
            });

            fetchLeafsTasks.Add(PrintProcess(pageItems));

            await Task.WhenAll(fetchLeafsTasks);
            Console.WriteLine($"Fetched {index.Items.Count} catalog pages, finding catalog leaves...");

            var filteredLeafItemsList = allLeafItemsBag
                .GroupBy(l => new PackageIdentity(l.Id, NuGetVersion.Parse(l.Version)))
                .Select(g => g.OrderByDescending(l => l.CommitTimeStamp).First())
                .Where(l => l.Type == "nuget:PackageDetails");

            var filteredLeafItems = new ConcurrentBag<CatalogLeaf>(filteredLeafItemsList);

            // Process all of the catalog leaf items.
            Console.WriteLine($"Processing {filteredLeafItems.Count} catalog leaves...");

            var processTasks = RunInParallel(async () =>
            {
                while (filteredLeafItems.TryTake(out var leaf))
                {
                    await ProcessCatalogLeafAsync(leaf);
                }
            });

            processTasks.Add(PrintProcess(filteredLeafItems));

            await Task.WhenAll(processTasks);

            Console.WriteLine();
            Console.WriteLine("Done");
        }

        private static async Task PrintProcess<T>(ConcurrentBag<T> items)
        {
            while (items.Count > 0)
            {
                Console.WriteLine($"{items.Count} leaves remaining...");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        private static async Task<Uri> GetCatalogIndexUrlAsync(HttpClient httpClient, string sourceUrl)
        {
            var client = new ServiceIndexClient(httpClient);

            var serviceIndex = await client.GetServiceIndexAsync(sourceUrl);
            var resource = serviceIndex.Resources.First(r => r.Type == "Catalog/3.0.0");

            return new Uri(resource.Url);
        }

        private static async Task ProcessCatalogLeafAsync(CatalogLeaf leaf)
        {
            var packageId = leaf.Id;
            var packageVersion = NuGetVersion.Parse(leaf.Version).ToNormalizedString();

            Console.WriteLine($"{packageId} {packageVersion}");

            await Task.Yield();
        }

        private static List<Task> RunInParallel(Func<Task> work)
        {
            return Enumerable
                .Range(0, MaxDegreeOfParallelism)
                .Select(i => work())
                .ToList();
        }
    }
}
