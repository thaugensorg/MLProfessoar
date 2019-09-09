using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;


namespace semisupervisedFramework
{
    public static class TestTrigger
    {
        [FunctionName("TestTrigger")]
        public static void Run([BlobTrigger("test/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string name, ILogger log)
        {
            Engine engine = new Engine(log);
            Test test = new Test(engine, log);
            string result = "";
            Task<string> noTrainedModelTest = Task.FromResult(new string(result));

            string StorageConnection = engine.GetEnvironmentVariable("AzureWebJobsStorage", log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("test");
            CloudBlockBlob testInitiationBlob = testDataContainer.GetBlockBlobReference(name);

            switch (name)
            {
                case "test.test":

                    testInitiationBlob.DeleteIfExists();
                    noTrainedModelTest = test.NoTrainedModelTest();
                    break;

                case "NoTrainedModelTest":
                    testInitiationBlob.DeleteIfExists();
                    noTrainedModelTest = test.NoTrainedModelTest();
                    break;
            }

            if (noTrainedModelTest.IsCompleted)
            {
                if (string.IsNullOrEmpty(noTrainedModelTest.Result))
                {
                    log.LogInformation($"All tests passed!");
                }
                else
                {
                    log.LogInformation($"Some test failures exist: \n {noTrainedModelTest.Result}");
                }
            }
            log.LogInformation($"TestTrigger complete.");
        }
    }
}
