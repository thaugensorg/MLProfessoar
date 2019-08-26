using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Spatial;
using Microsoft.Extensions.Logging;

using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using semisupervisedFramework.Models;


// This sample shows how to delete, create, upload documents and query an index
// http://danielcoding.net/working-with-azure-search-in-c-net/
// https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
// https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/

namespace semisupervisedFramework
{
    class Search
    {
        public static void InitializeSearch()
        {
            ILoggerFactory logger = (ILoggerFactory)new LoggerFactory();
            ILogger log = logger.CreateLogger("Search");
            string SearchApiKey = Engine.GetEnvironmentVariable("blobSearchKey", log);
            string indexName = Engine.GetEnvironmentVariable("bindinghash", log);

            //instanciates a serch client and creates the index.
            SearchServiceClient serviceClient = Initialize("semisupervisedblobsearch", "blobindex", SearchApiKey);
            DataSource JsonBlob = CreateBlobSearchDataSource(log);
            serviceClient.DataSources.CreateOrUpdateAsync(JsonBlob).Wait();
            Index BlobIndex = CreateIndex(serviceClient, "blobindex");
            Indexer BlobIndexer = CreateBlobIndexer(serviceClient, BlobIndex, "json-blob");
            serviceClient.Indexers.RunAsync(BlobIndexer.Name).Wait();

        }

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

                Fields = FieldBuilder.BuildForType<JsonModel>()
            };

            //new Field("blobInfo", DataType.Complex),
            //Fields = new []
            //{
            //    new Field("id", DataType.String)                  { IsKey = true, IsRetrievable = true},
            //},
            //new Field("name", DataType.String)                { IsRetrievable = true},
            //new Field("url", DataType.String)                 { IsRetrievable = true},
            //new Field("hash", DataType.String)                { IsRetrievable = true, IsSearchable = true},
            //new Field("modified", DataType.DateTimeOffset)    { IsRetrievable = true},
            //}
            //};

            indexDefinition.Validate();

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
            string StorageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage", log);

            DataSource dataSource = DataSource.AzureBlobStorage(
                name: "json-blob",
                storageConnectionString: StorageConnection,
                containerName: "json");
            return dataSource;
        }

        public static Indexer CreateBlobIndexer(SearchServiceClient searchService, Index index, string dataSourceName)
        {
            Indexer indexer = new Indexer(
                name: "blob-indexer",
                dataSourceName: dataSourceName,
                targetIndexName: index.Name,
                schedule: new IndexingSchedule(TimeSpan.FromMinutes(5)));

            bool exists = searchService.Indexers.Exists(indexer.Name);
            if (exists)
            {
                searchService.Indexers.ResetAsync(indexer.Name);
            }
            else
            {
                // this line seems to error if the indexer is reset
                //searchService.Indexers.CreateOrUpdateAsync(indexer);
                searchService.Indexers.CreateOrUpdate(indexer);
            }

            return indexer;
        }

        public static SearchIndexClient CreateSearchIndexClient(string indexName, ILogger log)
        {
            string SearchApiKey = Engine.GetEnvironmentVariable("blobSearchKey", log);
            string SearchServiceName = Engine.GetEnvironmentVariable("SearchServiceName", log);

            SearchIndexClient indexClient = new SearchIndexClient(SearchServiceName, indexName, new SearchCredentials(SearchApiKey));
            return indexClient;
        }
    }
}
