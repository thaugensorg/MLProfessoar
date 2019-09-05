using System;
using System.IO;
using System.Net.Http;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;



namespace semisupervisedFramework
{
    public static class BlobIndexer
    {
        [FunctionName("IndexJsonBlobs")]
        public static void Run([BlobTrigger("json/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            Engine engine = new Engine(log);

            log.LogInformation($"\nInitiating indexing of: {blobName}");

            Search search = new Search(engine, log);
            Model model = new Model(engine, search, log);

            try
            {
                search.RunIndexer();

                //string searchServiceName = engine.GetEnvironmentVariable("SearchServiceName", log);
                //string blobSearchIndexerName = engine.GetEnvironmentVariable("blobSearchIndexerName", log);
                //string url = $"https://{searchServiceName}.search.windows.net/indexers/{blobSearchIndexerName}/run?api-version=2019-05-06";

                //MultipartFormDataContent header = new MultipartFormDataContent();
                //string blobSearchKey = engine.GetEnvironmentVariable("blobSearchKey", log);
                //HttpContent blobSearchKeyContent = new StringContent(blobSearchKey);
                //header.Add(blobSearchKeyContent, "api-key");

                //string indexerResponse = engine.GetHttpResponseString(url, header);
                //log.LogInformation($"Azure function, IndexJsonBlobs, indexed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
            }
            catch
            {
                log.LogInformation($"\nAzure Function, IndexJsonBlobs, failed to index data blob: {blobName}");
            }
        }
    }
}
