using System;
using System.IO;

using Microsoft.Azure.WebJobs;
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
            }
            catch
            {
                log.LogInformation($"\nAzure Function, IndexJsonBlobs, failed to index data blob: {blobName}");
            }
        }
    }
}
