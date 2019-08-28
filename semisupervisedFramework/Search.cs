﻿using System;
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

        public Search(Engine engine, ILogger log)
        {
            _Log = log;
            _Engine = engine;
        }

        // *****TODO***** should search be static or instanciable?
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
        public CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName, ILogger log)
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
                log.LogInformation("\nNo blob " + blobName + " found in " + containerName + " ", e.Message);
                return null;
            }
        }


        public void InitializeSearch()
        {
            ILoggerFactory logger = (ILoggerFactory)new LoggerFactory();
            ILogger log = logger.CreateLogger("Search");
            string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string indexName = _Engine.GetEnvironmentVariable("bindinghash", log);

            //instanciates a serch client and creates the index.
            SearchServiceClient serviceClient = Initialize("semisupervisedblobsearch", "blobindex", SearchApiKey);
            DataSource JsonBlob = CreateBlobSearchDataSource(log);
            serviceClient.DataSources.CreateOrUpdateAsync(JsonBlob).Wait();
            Index BlobIndex = CreateIndex(serviceClient, "blobindex");
            Indexer BlobIndexer = CreateBlobIndexer(serviceClient, BlobIndex, "json-blob");
            serviceClient.Indexers.RunAsync(BlobIndexer.Name).Wait();

        }

        // https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/
        public SearchServiceClient Initialize(string serviceName, string indexName, string apiKey)
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
            string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", log);

            DataSource dataSource = DataSource.AzureBlobStorage(
                name: "json-blob",
                storageConnectionString: StorageConnection,
                containerName: "json");
            return dataSource;
        }

        public Indexer CreateBlobIndexer(SearchServiceClient searchService, Index index, string dataSourceName)
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

        public SearchIndexClient CreateSearchIndexClient(string indexName, ILogger log)
        {
            string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string SearchServiceName = _Engine.GetEnvironmentVariable("SearchServiceName", log);

            SearchIndexClient indexClient = new SearchIndexClient(SearchServiceName, indexName, new SearchCredentials(SearchApiKey));
            return indexClient;
        }
    }
}
