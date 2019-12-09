using System;
using System.IO;
using System.Text;

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
                    Search search = new Search(engine);
                    Model model = new Model(engine, search);

                    // Instanciate test labeling class that matches the configured labeling solution.
                    // Note: test labeling class must follow the nameing convention - {environment variable naming soluition name}TestLabeler
                    // If this convention is not followed ML Professoar cannot find the test labeling class to instanciate.
                    string labelingSolutionName = engine.GetEnvironmentVariable("labelingSolutionName");
                    Type classType = Type.GetType($"semisupervisedFramework.{labelingSolutionName}TestLabeler");
                    Test test = (Test)Activator.CreateInstance(classType, new Object[] { engine, search, model });

                    // Inititialize test result variables
                    string noTrainedModelTestResults = "";
                    string trainModelTestResults = "";
                    string evaluatePassingDataTestResults = "";
                    string evaluateFailingDataTestResults = "";
                    string labelDataTestResults = "";
                    string loadLabeledDataTestResults = "";
                    string LoadLabelsTestResults = "";

                    // get a reference to the invocation blob file so that it can be deleted after the test is launched.
                    string storageConnection = engine.GetEnvironmentVariable("AzureWebJobsStorage");
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                    CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testinvocation");
                    CloudBlockBlob testInitiationBlob = testDataContainer.GetBlockBlobReference(name);

                    string testResults = "Initialized";

                    switch (name)
                    {
                        case "TestAll.test":

                            testInitiationBlob.DeleteIfExists();
                            noTrainedModelTestResults = await test.NoTrainedModel();
                            labelDataTestResults = await test.LabelData();
                            loadLabeledDataTestResults = await test.LoadLabeledData();
                            trainModelTestResults = await test.TrainModel();
                            evaluatePassingDataTestResults = await test.EvaluatePassingData();
                            evaluateFailingDataTestResults = await test.EvaluateFailingData();

                            if (noTrainedModelTestResults.Contains("Failed:") || trainModelTestResults.Contains("Failed:") || evaluatePassingDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{noTrainedModelTestResults}\n{labelDataTestResults}\n{trainModelTestResults}\n{evaluatePassingDataTestResults}\n{evaluateFailingDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results:\n{noTrainedModelTestResults}\n{labelDataTestResults}\n{trainModelTestResults}\n{evaluatePassingDataTestResults}\n{evaluateFailingDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;

                        case "NoTrainedModel.test":
                            testInitiationBlob.DeleteIfExists();
                            noTrainedModelTestResults = await test.NoTrainedModel();
                            if (noTrainedModelTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{noTrainedModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {noTrainedModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            break;
                        case "TrainModel.test":
                            testInitiationBlob.DeleteIfExists();
                            trainModelTestResults = await test.TrainModel();
                            if (trainModelTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{trainModelTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {trainModelTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;
                        case "EvaluatePassingData.test":
                            testInitiationBlob.DeleteIfExists();
                            evaluatePassingDataTestResults = await test.EvaluatePassingData();
                            if (evaluatePassingDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{evaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {evaluatePassingDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;
                        case "EvaluateFailingData.test":
                            testInitiationBlob.DeleteIfExists();
                            evaluateFailingDataTestResults = await test.EvaluateFailingData();
                            if (evaluateFailingDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{evaluateFailingDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {evaluateFailingDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;

                        case "LabelData.test":
                            testInitiationBlob.DeleteIfExists();
                            labelDataTestResults = await test.LabelData();
                            if (labelDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{labelDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {labelDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;

                        case "LoadLabeledData.test":
                            testInitiationBlob.DeleteIfExists();
                            loadLabeledDataTestResults = await test.LoadLabeledData();
                            if (loadLabeledDataTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{loadLabeledDataTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {loadLabeledDataTestResults}";
                                log.LogInformation(testResults);
                            }

                            break;

                        case "LoadLabels.test":
                            testInitiationBlob.DeleteIfExists();
                            LoadLabelsTestResults = await test.LoadLabels();
                            if (LoadLabelsTestResults.Contains("Failed:"))
                            {
                                testResults = $"\nFailed: Some test failures exist:\n{LoadLabelsTestResults}";
                                log.LogInformation(testResults);
                            }
                            else
                            {
                                testResults = $"\nAll test passed! with results: {LoadLabelsTestResults}";
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
