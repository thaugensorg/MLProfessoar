using System;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Spatial;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Rest.Azure;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


// This sample shows how to delete, create, upload documents and query an index
// http://danielcoding.net/working-with-azure-search-in-c-net/
// https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
// https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/

namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class marshalls the Azure Search engine which is used to construct blobs from data blob MD5 hash
    // values.  All files that are "touched" by ML Professoar have a json blob file created that includes information
    // about the iteration and results of the action AND the MD5 hash that binds data blobs and json blobs together.
    //**********************************************************************************************************

    class Search
    {
        private ILogger _Log;
        private Engine _Engine;
        private SearchServiceClient _ServiceClient;

        public Search(Engine engine, ILogger log)
        {
            _Log = log;
            _Engine = engine;
            string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string searchName = _Engine.GetEnvironmentVariable("blobSearchServiceName", log);
            _ServiceClient = Initialize(searchName, SearchApiKey);
        }

        // *****TODO***** update index to track deleted items or we will get duplicate hash entries if the json data is reloaded.
        public FrameworkBlob GetBlob(string Type, string dataBlobMD5)
        {
            switch (Type)
            {
                case "data":
                    return new DataBlob(dataBlobMD5, _Engine, this, _Log);

                case "json":
                    return new JsonBlob(dataBlobMD5, _Engine, this, _Log);

                default:
                    throw (new MissingRequiredObject($"\nInvalid blob type: {Type}"));

            }
        }

        //Gets a reference to a specific blob using container and blob names as strings
        public CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            try
            {
                CloudBlobClient BlobClient = account.CreateCloudBlobClient();
                CloudBlobContainer Container = BlobClient.GetContainerReference(containerName);
                Container.CreateIfNotExistsAsync().Wait();

                CloudBlockBlob Blob = Container.GetBlockBlobReference(blobName);

                return Blob;
            }
            catch (Exception e)
            {
                _Log.LogInformation("\nNo blob " + blobName + " found in " + containerName + " ", e.Message);
                return null;
            }
        }

        public void RunIndexer()
        {
            try
            {
                string blobSearchIndexerName = _Engine.GetEnvironmentVariable("blobSearchIndexerName", _Log);
                //Indexer blobSearchIndexer = _ServiceClient.Indexers.Get(blobSearchIndexerName);
                _ServiceClient.Indexers.RunAsync(blobSearchIndexerName);
            }
            catch (CloudException e) when (e.Response.StatusCode == (HttpStatusCode)429)
            {
                Console.WriteLine($"Failed to run indexer: {0}", e.Response.Content);
            }
        }

        public void InitializeSearch()
        {
            ILoggerFactory logger = (ILoggerFactory)new LoggerFactory();
            ILogger log = logger.CreateLogger("Search");
            string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string indexName = _Engine.GetEnvironmentVariable("blobSearchIndexName", log);
            string searchName = _Engine.GetEnvironmentVariable("blobSearchServiceName", log);
            string searchDataSourceName = _Engine.GetEnvironmentVariable("blobsearchdatasource", log);

            //instanciates a serch client and creates the index.
            SearchServiceClient serviceClient = Initialize(searchName, SearchApiKey);
            DataSource JsonBlob = CreateBlobSearchDataSource(log);
            serviceClient.DataSources.CreateOrUpdateAsync(JsonBlob).Wait();
            Index BlobIndex = CreateIndex(serviceClient, indexName);
            Indexer BlobIndexer = CreateBlobIndexer(serviceClient, BlobIndex, searchDataSourceName);
            serviceClient.Indexers.RunAsync(BlobIndexer.Name).Wait();

        }

        // https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/
        public SearchServiceClient Initialize(string serviceName, string apiKey)
        {
            SearchServiceClient serviceClient = new SearchServiceClient(serviceName, new SearchCredentials(apiKey));

            return serviceClient;
        }

        public Index CreateIndex(SearchServiceClient client, string indexName)
        {
            // https://docs.microsoft.com/en-us/rest/api/searchservice/create-index

            var indexDefinition = new Index()
            {
                Name = indexName,

                Fields = FieldBuilder.BuildForType<JsonBlob>()
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

        private void DeleteIfIndexExist(SearchServiceClient client, string indexName)
        {
            if (client.Indexes.Exists(indexName))
            {
                client.Indexes.Delete(indexName);
            }
        }

        public DataSource CreateBlobSearchDataSource(ILogger log)
        {
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", log);
            string searchDataSourceName = _Engine.GetEnvironmentVariable("blobsearchdatasource", log);
            string jsonStorageContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", log);

            DataSource dataSource = DataSource.AzureBlobStorage(
                name: searchDataSourceName,
                storageConnectionString: storageConnection,
                containerName: jsonStorageContainerName);
            return dataSource;
        }

        public Indexer CreateBlobIndexer(SearchServiceClient searchService, Index index, string dataSourceName)
        {
            string blobSearchIndexerName = _Engine.GetEnvironmentVariable("blobSearchIndexerName", _Log);

            Indexer indexer = new Indexer(
                name: blobSearchIndexerName,
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

        public SearchIndexClient CreateSearchIndexClient(string indexName, ILogger log)
        {
            string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string SearchServiceName = _Engine.GetEnvironmentVariable("blobSearchServiceName", log);

            SearchIndexClient indexClient = new SearchIndexClient(SearchServiceName, indexName, new SearchCredentials(SearchApiKey));
            return indexClient;
        }
    }
}
