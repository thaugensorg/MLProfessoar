using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Spatial;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;


using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

// http://danielcoding.net/working-with-azure-search-in-c-net/
// https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
// https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/

namespace semisupervisedFramework
{
    class Search
    {
        // This sample shows how to delete, create, upload documents and query an index
        static void InitializeSearch(ILogger log)
        {
            string SearchApiKey = Environment.GetEnvironmentVariable("blobSearchKey", log);
            string indexName = Environment.GetEnvironmentVariable("bindinghash", log);

            //instanciates a serch client and creates the index.
            SearchServiceClient serviceClient = Helper.Initialize("semisupervisedblobsearch", "blobindex", SearchApiKey);
            DataSource JsonBlob = Helper.CreateBlobSearchDataSource(log);
            serviceClient.DataSources.CreateOrUpdateAsync(JsonBlob).Wait();
            Index BlobIndex = Helper.CreateIndex(serviceClient, "blobindex");
            Task<Indexer> BlobIndexer = Helper.CreateBlobIndexer(serviceClient, BlobIndex, "json-blob");
            serviceClient.Indexers.RunAsync(BlobIndexer)

        }
    }
    public class DataBlob
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Url { get; set; }
        public string Hash { get; set; }
        public DateTimeOffset Modified { get; set; }

        public override string ToString()
        {
            return $"Id: {Id}\tName: {Name}\tURL: {Url}\tHash: {Hash}\tModified: {Modified}";
        }
    }
    public static class Helper
    {
        // https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/
        public static SearchServiceClient Initialize(string serviceName, string indexName, string apiKey)
        {
            SearchServiceClient serviceClient = new SearchServiceClient(serviceName, new SearchCredentials(apiKey));

            return serviceClient;
        }

        public static Index CreateIndex(SearchServiceClient client, string indexName)
        {
            // https://docs.microsoft.com/en-us/rest/api/searchservice/create-index
            var indexDefinition = new Index()
            {
                Name = indexName,
                Fields = new[]
                {
                    new Field("id", DataType.String)                  { IsKey = true},
                    new Field("name", DataType.String)                { IsRetrievable = true},
                    new Field("url", DataType.String)                 { IsRetrievable = true},
                    new Field("hash", DataType.String)                { IsSearchable = true},
                    new Field("modified", DataType.DateTimeOffset)    { IsRetrievable = true},
                }
            };

            DeleteIfIndexExist(client, indexName);

            client.Indexes.Create(indexDefinition);

            return client.Indexes.Get(indexName);
        }

        private static void DeleteIfIndexExist(SearchServiceClient client, string indexName)
        {
            if (client.Indexes.Exists(indexName))
            {
                client.Indexes.Delete(indexName);
            }
        }

        public static DataSource CreateBlobSearchDataSource(ILogger log)
        {
            string StorageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage", log);

            DataSource dataSource = DataSource.AzureBlobStorage(
                name: "json-blob",
                storageConnectionString: StorageConnection,
                containerName: "json");
            return dataSource;
        }

        public static async Task<Indexer> CreateBlobIndexer(SearchServiceClient searchService, Index index, string dataSourceName)
        {
            var indexer = new Indexer(
                name: "blob-indexer",
                dataSourceName: dataSourceName,
                targetIndexName: index.Name,
                schedule: new IndexingSchedule(TimeSpan.FromMinutes(1)));

            var exists = await searchService.Indexers.ExistsAsync(indexer.Name);
            if (exists)
            {
                await searchService.Indexers.ResetAsync(indexer.Name);
            }

            await searchService.Indexers.CreateOrUpdateAsync(indexer);

            return indexer;
        }
    }
}
