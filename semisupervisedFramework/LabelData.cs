using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace semisupervisedFramework
{
    public static class LabelData
    {
        [FunctionName("LabelData")]
        public static void Run([BlobTrigger("labelingoutput/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            Engine engine = new Engine(log);

            log.LogInformation($"\nInitiating labeling of: {blobName}");

            Search search = new Search(engine, log);
            Model model = new Model(engine, search, log);

            CloudStorageAccount storageAccount = engine.StorageAccount;
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            string pendingSupervisionStorageContainerName = engine.GetEnvironmentVariable("labelingOutputStorageContainerName", log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            CloudBlockBlob boundDataBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(blobName);
            // You must 'touch' the blob before you can access properties or they are all null.
            string boundJsonFileName = "";
            if (boundDataBlob.Exists())
            {
                boundJsonFileName = engine.GetEncodedHashFileName(boundDataBlob.Properties.ContentMD5.ToString());
            }

            // instanciate bound json blob and get its Json.
            JsonBlob boundJsonBlob = new JsonBlob(boundJsonFileName, engine, search, log);

            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
        }
    }
}
