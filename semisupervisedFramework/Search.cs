using System;
using System.Net;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Search;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Rest.Azure;


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

    public class Search
    {
        private Engine _engine;
        private SearchServiceClient _ServiceClient;

        public Search(Engine engine)
        {
            _engine = engine;
            string SearchApiKey = _engine.GetKeyVaultSecret("BlobSearchKey").Result;

            //string SearchApiKey = _Engine.GetEnvironmentVariable("blobSearchKey", log);
            string searchName = _engine.GetEnvironmentVariable("blobSearchServiceName");
            _ServiceClient = Initialize(searchName, SearchApiKey);
        }

        // *****TODO***** update index to track deleted items or we will get duplicate hash entries if the json data is reloaded.

        //Gets a reference to a specific blob using container and blob names as strings
        public CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            try
            {
                CloudBlobClient blobClient = account.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference(containerName);
                container.CreateIfNotExistsAsync().Wait();

                CloudBlockBlob Blob = container.GetBlockBlobReference(blobName);

                return Blob;
            }
            catch (Exception e)
            {
                _engine.Log.LogInformation($"\nNo blob {blobName} found in {containerName} ", e.Message);
                return null;
            }
        }

        public void RunIndexer()
        {
            try
            {
                string blobSearchIndexerName = _engine.GetEnvironmentVariable("blobSearchIndexerName");
                _ServiceClient.Indexers.RunAsync(blobSearchIndexerName);
            }
            catch (CloudException e) when (e.Response.StatusCode == (HttpStatusCode)429)
            {
                Console.WriteLine($"\nFailed to run indexer: {0}", e.Response.Content);
            }
        }

        // https://cmatskas.com/indexing-and-searching-sql-server-data-with-azure-search/
        public SearchServiceClient Initialize(string serviceName, string apiKey)
        {
            SearchServiceClient serviceClient = new SearchServiceClient(serviceName, new SearchCredentials(apiKey));

            return serviceClient;
        }

        public async Task<SearchIndexClient> CreateSearchIndexClient(string indexName)
        {
            string SearchApiKey = await _engine.GetKeyVaultSecret("BlobSearchKey");
            string SearchServiceName = _engine.GetEnvironmentVariable("blobSearchServiceName");

            SearchIndexClient indexClient = new SearchIndexClient(SearchServiceName, indexName, new SearchCredentials(SearchApiKey));
            return indexClient;
        }
    }
}
