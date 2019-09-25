using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
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
            try
            {
                if (name.Contains(".test"))
                {
                    Engine engine = new Engine(log);
                    Search search = new Search(engine, log);
                    Test test = new Test(engine, search, log);
                    string noTrainedModelTestResults = "";
                    string trainModelTestResults = "";
                    string EvaluatePassingDataTestResults = "";

                    // get a reference to the invocation blob file so that it can be deleted after the test is launched.
                    string StorageConnection = engine.GetEnvironmentVariable("AzureWebJobsStorage", log);
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                    CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testinvocation");
                    CloudBlockBlob testInitiationBlob = testDataContainer.GetBlockBlobReference(name);

                    string testResults = "Initialized";

                    switch (name)
                    {
                        case "TestAll.test":

                            testInitiationBlob.DeleteIfExists();
                            noTrainedModelTestResults = await test.NoTrainedModelTest();
                            trainModelTestResults = await test.TrainModelTest();
                            EvaluatePassingDataTestResults = await test.EvaluatePassingDataTest();

                            if (noTrainedModelTestResults.Contains("Failed:") || trainModelTestResults.Contains("Failed:") || EvaluatePassingDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"Failed: Some test failures exist:\n{noTrainedModelTestResults}\n{trainModelTestResults}\n{EvaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"All test passed! with results:\n{noTrainedModelTestResults}\n{trainModelTestResults}\n{EvaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;

                        case "NoTrainedModel.test":
                            testInitiationBlob.DeleteIfExists();
                            noTrainedModelTestResults = await test.NoTrainedModelTest();
                            if (noTrainedModelTestResults.Contains("Failed:"))
                            {
                                testResults = $"Failed: Some test failures exist:\n{noTrainedModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"All test passed! with results: {noTrainedModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            break;
                        case "TrainModel.test":
                            testInitiationBlob.DeleteIfExists();
                            trainModelTestResults = await test.TrainModelTest();
                            if (trainModelTestResults.Contains("Failed:"))
                            {
                                testResults = $"Failed: Some test failures exist:\n{trainModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"All test passed! with results: {trainModelTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;
                        case "EvaluatePassingData.test":
                            testInitiationBlob.DeleteIfExists();
                            EvaluatePassingDataTestResults = await test.EvaluatePassingDataTest();
                            if (EvaluatePassingDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"Failed: Some test failures exist:\n{EvaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"All test passed! with results: {EvaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;
                    }

                    //await Task.Delay(75000);
                    //int waitingAttempts = 0;
                    //do
                    //{
                    //    waitingAttempts++;
                    //    await Task.Delay(5000);
                    //    log.LogInformation($"noTrainedModelTest checked {waitingAttempts} times.  Will check 30 times before continuing.");

                    //} while (waitingAttempts <= 30);


                    CloudBlockBlob testResultsCloudBlob = testDataContainer.GetBlockBlobReference("TestResults-" + Guid.NewGuid().ToString() + ".txt");
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
            catch (Exception e)
            {
                log.LogInformation($"TestTrigger failed with {e.Message}.");
            }
        }
    }
}
