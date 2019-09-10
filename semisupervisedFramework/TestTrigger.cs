using System;
using System.IO;
using System.Text;
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
        public async static void Run([BlobTrigger("testinvocation/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string name, ILogger log)
        {
            if (name.Contains(".test"))
            {
                Engine engine = new Engine(log);
                Test test = new Test(engine, log);
                string result = "";
                Task<string> noTrainedModelTest = Task.FromResult(new string(result));

                // get a reference to the invocation blob file so that it can be deleted after the test is launched.
                string StorageConnection = engine.GetEnvironmentVariable("AzureWebJobsStorage", log);
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testinvocation");
                CloudBlockBlob testInitiationBlob = testDataContainer.GetBlockBlobReference(name);

                switch (name)
                {
                    case "TestAll.test":

                        testInitiationBlob.DeleteIfExists();
                        noTrainedModelTest = test.NoTrainedModelTest();
                        break;

                    case "NoTrainedModel":
                        testInitiationBlob.DeleteIfExists();
                        noTrainedModelTest = test.NoTrainedModelTest();
                        break;
                }

                await Task.Delay(75000);
                int waitingAttempts = 0;
                do
                {
                    waitingAttempts++;
                    await Task.Delay(5000);
                    log.LogInformation($"noTrainedModelTest checked {waitingAttempts} times.  Will check 30 times before continuing.");

                } while (!(noTrainedModelTest.IsCompleted) && waitingAttempts <= 30);

                string testResults;

                if (noTrainedModelTest.IsCompleted)
                {
                    if (noTrainedModelTest.Result.Contains("Passed:"))
                    {
                        testResults = $"All test passed! with results: {noTrainedModelTest.Result}";
                        log.LogInformation(testResults);
                    }
                    else
                    {
                        testResults = $"Failed: Some test failures exist: \n {noTrainedModelTest.Result}";
                        log.LogInformation(testResults);
                    }
                }
                else
                {
                    testResults = $"Failed: NoTrainedModelTest did not complete.";
                    log.LogInformation(testResults);
                }
                CloudBlockBlob testResultsCloudBlob = testDataContainer.GetBlockBlobReference("TestTesults-" + Guid.NewGuid().ToString() + ".txt");
                Stream MemStream = new MemoryStream(Encoding.UTF8.GetBytes(testResults));
                if (MemStream.Length != 0)
                {
                    testResultsCloudBlob.UploadFromStream(MemStream);
                }
                else
                {
                    throw (new ZeroLengthFileException("\nencoded JSON memory stream is zero length and cannot be writted to blob storage"));
                }


                log.LogInformation($"TestTrigger complete.");
            }
        }
    }
}
