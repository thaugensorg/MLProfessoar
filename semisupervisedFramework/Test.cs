using System;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;



namespace semisupervisedFramework
{
    class Test
    {
        private ILogger _Log;
        private Engine _Engine;
        private Search _Search;
        private Model _Model;

        public Test(Engine engine, Search search, ILogger log)
        {
            _Engine = engine;
            _Search = search;
            _Model = new Model(engine, search, log);
            _Log = log;
        }

        // C# await and azync tutorial https://www.youtube.com/watch?v=C5VhaxQWcpE
        public async Task<string> NoTrainedModelTest()
        {

            // Get a reference to the test data container
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
            CloudBlobContainer pendingEvaluationContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop over items within the container and move them to pending evaluation container
            await _Engine.CopyBlobsFromContainerToContainer(testDataContainer, pendingEvaluationContainer);

            //check that all items have been moved to the pending supervision container
            string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);

            // Initialize loop control variables
            int verifiedBlobs = 0;
            int checkLoops = 0;

            // Loop through all blobs in test data container and ensure there is a corresponding file in the expectged pending supervision container.
            do
            {
                verifiedBlobs = 0;
                foreach (IListBlobItem item in testDataContainer.ListBlobs(null, false))
                {
                    if (item is CloudBlockBlob verificationBlob)
                    {
                        CloudBlockBlob expectedBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(verificationBlob.Name);
                        if (expectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 20)
                            {
                                return $"Passed: 20 blobs verified in {pendingSupervisionStorageContainerName}";
                            }
                        }
                    }
                }

                // If after making a pass through all of the test container blobs the test has not passed wait and then check again.  Because the code
                // does not invoke the Orchestration Engine directly we cannot await the call to evaluate data so we have to delay and try again.
                // Given Azure Functions performance we are delaying 5 seconds.  If Azure function performance improves this time can be reduced.
                await Task.Delay(5000);
                checkLoops++;

            // Keep looping until either the test passes or 10 attempts have been made.  *****TODO***** this should be externalized in the future for performance tuning.
            } while (verifiedBlobs <= 20 && checkLoops <= 10);

            return $"Failed: NoTrainedModelTest only found {verifiedBlobs} in {pendingSupervisionStorageContainerName} but 20 were expected.";
        }

        public async Task<string> LabelDataTest()
        {
            // Get a azure storage client
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            //get references to the test label data and the location the test blobs needs to be to run tests.
            string jsonStorageContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
            CloudBlobContainer jsonStorageContainer = blobClient.GetContainerReference(jsonStorageContainerName);
            CloudBlockBlob dataLabelingTagsBlob = jsonStorageContainer.GetBlockBlobReference("LabelingTags.json");
            string testDataContainerName = "testdata/testjson";
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference(testDataContainerName);
            CloudBlockBlob testDataLabelingTagsBlob = testDataContainer.GetBlockBlobReference("LabelingTags.json");

            //copy the test labeling tags blob to the expected location.
            await dataLabelingTagsBlob.StartCopyAsync(testDataLabelingTagsBlob);

            // mock the supervision loop by labeling data using the blob name hemlock or japenese cherry.
            await LabelData();

            string labeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);

            int verifiedBlobs = 0;
            string response = "Failed: response initialized but not updated";
            foreach (IListBlobItem item in labeledDataStorageContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob verificationBlob)
                {
                    verifiedBlobs++;
                    if (verifiedBlobs == 20)
                    {
                        response = $"Passed: 20 blobs verified in {labeledDataStorageContainerName}";
                    }
                    else
                    {
                        response = $"Failed: {verifiedBlobs} found in {labeledDataStorageContainerName} 20 were expected.";
                    }
                }
            }

            return response;
        }

        public async Task<string> TrainModelTest()
        {

            // Train the model using the core training process
            await _Model.TrainingProcess();

            //*****TODO*****Verify labels were loaded by calling python model

            //Verify labeled data was loaded by calling python model

            //Verify the model was trained and an iteration assigned by calling python model

            return "Passed: Model trained.";
        }

        public async Task<string> EvaluateFailingData()
        {
            // Establish a storage connection
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            CloudBlobDirectory failingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("failing");
            string evaluatedDataStorageContainerName = _Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName", _Log);
            CloudBlobContainer evaluatedDataStorageContainer = blobClient.GetContainerReference(evaluatedDataStorageContainerName);
            string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
            CloudBlobContainer pendingEvaluationStorageContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop through failing test data and call evaluate by copying blobs to pending evaluation container.
            foreach (IListBlobItem item in failingEvaluationTestDataDirectory.ListBlobs())
            {
                if (item is CloudBlockBlob testDataBlob)
                {
                    string name = testDataBlob.Name.Split('/')[1];
                    CloudBlockBlob evaluateDataBlob = pendingEvaluationStorageContainer.GetBlockBlobReference(name);
                    await _Engine.CopyAzureBlobToAzureBlob(storageAccount, testDataBlob, evaluateDataBlob);
                }
            }

            // wait 30 seconds for the evaluation to complete
            await Task.Delay(30000);

            int verifiedBlobs = 0;
            int checkLoops = 0;
            string response = "";

            do
            {
                verifiedBlobs = 0;

                // Loop back through the pass test data container and make sure their is a matching blob in the evaluated data container
                foreach (IListBlobItem item in failingEvaluationTestDataDirectory.ListBlobs())
                {
                    if (item is CloudBlockBlob testDataBlob)
                    {
                        string name = testDataBlob.Name.Split('/')[1];
                        CloudBlockBlob expectedBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(name);
                        if (expectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 2)
                            {
                                return response + $"\nPassed: 2 blobs did not pass evaluation and were verified in {pendingSupervisionStorageContainerName}";
                            }
                        }

                        await Task.Delay(500);
                    }
                }

                await Task.Delay(1000);
                checkLoops++;

                if (checkLoops > 5)
                {
                    response = response + $"\n{verifiedBlobs} blobs found in {pendingSupervisionStorageContainerName} when 2 were expected.";
                }

            } while (verifiedBlobs < 2 && checkLoops <= 5);

            return "Failed: " + response;

        }

        public async Task<string> EvaluatePassingDataTest()
        {
            // Establish a storage connection
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            CloudBlobDirectory passingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("passing");
            string evaluatedDataStorageContainerName = _Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName", _Log);
            CloudBlobContainer evaluatedDataStorageContainer = blobClient.GetContainerReference(evaluatedDataStorageContainerName);
            string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
            CloudBlobContainer pendingEvaluationStorageContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop through passing test data and call evaluate by copying blobs to pending evaluation container.
            foreach (IListBlobItem item in passingEvaluationTestDataDirectory.ListBlobs(true))
            {
                if (item is CloudBlockBlob testDataBlob)
                {
                    string name = testDataBlob.Name.Split('/')[1];
                    CloudBlockBlob evaluateDataBlob = pendingEvaluationStorageContainer.GetBlockBlobReference(name);
                    await _Engine.CopyAzureBlobToAzureBlob(storageAccount, testDataBlob, evaluateDataBlob);
                }
            }

            // wait 30 seconds for the evaluation to complete
            await Task.Delay(30000);

            int verifiedBlobs = 0;
            int checkLoops = 0;
            string response = "";

            do
            {
                verifiedBlobs = 0;

                // Loop back through the pass test data container and make sure their is a matching blob in the evaluated data container
                foreach (IListBlobItem item in passingEvaluationTestDataDirectory.ListBlobs())
                {
                    if (item is CloudBlockBlob testDataBlob)
                    {
                        string name = testDataBlob.Name.Split('/')[1];
                        CloudBlockBlob expectedBlob = evaluatedDataStorageContainer.GetBlockBlobReference(name);
                        if (expectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 7)
                            {
                                return $"\nPassed: {verifiedBlobs} passing blobs verified in {evaluatedDataStorageContainerName}";
                            }
                        }

                        await Task.Delay(500);
                    }
                }

                await Task.Delay(1000);
                checkLoops++;
                if (checkLoops > 4)
                {
                    response = $"\n{verifiedBlobs} found in {evaluatedDataStorageContainerName} when 7 were expected.";
                }

            } while (verifiedBlobs < 7 && checkLoops <= 5);

            return "Failed: " + response;

        }

        private async Task LabelData()
        {
            // Loop through all files in the pending supervision container and verify the bound json file has a label for the data file.
            string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Get references to the source container, pending supervision, and the destination container, labeled data
            string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            string labeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);
            string pendingNewModelStorageContainerName = _Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName", _Log);
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);

            // Loop through each blob in the pending supervision container and ensure it is properly labeled in the bound json blob and the raw data blob is moved to the labeled data container.
            foreach (IListBlobItem item in pendingSupervisionStorageContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob rawDataBlob)
                {
                    // hydrate the data blob's bound json blob and extract all labels.
                    string blobMd5 = rawDataBlob.Properties.ContentMD5;
                    JsonBlob jsonBlob = new JsonBlob(blobMd5, _Engine, _Search, _Log);
                    JObject jsonBlobJObject = JObject.Parse(jsonBlob.AzureBlob.DownloadText());
                    JArray labels = (JArray)jsonBlobJObject.SelectToken("labels");

                    // Loop through all of the applied labels and ensure one label matches the label in the file name such as Hemlock
                    if (labels != null)
                    {
                        foreach (JObject label in labels)
                        {
                            //*****TODO***** make condition support japense cherry too just parse off japense from the label and check for that 
                            if (rawDataBlob.Name.Contains(label.GetValue("label").ToString().ToLower()))
                            {
                                _Log.LogInformation($"\nJson blob {jsonBlob.AzureBlob.Name} already labeled with {label.GetValue("label").ToString().ToLower()}");
                                break;
                            }
                        //*****TODO***** add handling for case where the list of labels does not contain the current label in the file name.  Low priority as this condition should not exist with current test data.
                        }
                    }
                    else
                    {
                        labels = new JArray();
                        JProperty dataLabel = new JProperty("label");

                        // Add lable from file name to bound json blob
                        if (rawDataBlob.Name.Contains("hemlock"))
                        {
                            dataLabel.Value = "Hemlock";
                        }
                        else
                        {
                            dataLabel.Value = "Japanese Cherry";
                        }

                        JObject labelsObject = new JObject
                            {
                                dataLabel
                            };
                        labels.Add(labelsObject);
                        JProperty labelsJProperty = new JProperty("labels", labels);
                        jsonBlobJObject.Add(labelsJProperty);

                        await _Engine.UploadJsonBlob(jsonBlob.AzureBlob, jsonBlobJObject);

                    } //done updating labeling information in json blob

                    // copy current raw blob working file from pending supervision to labeled data AND pending new model containers
                    CloudBlockBlob destinationBlob = labeledDataStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
                    CloudBlockBlob pendingNewModelDestinationBlob = pendingNewModelStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
                    await _Engine.CopyAzureBlobToAzureBlob(storageAccount, rawDataBlob, pendingNewModelDestinationBlob);
                    await _Engine.MoveAzureBlobToAzureBlob(storageAccount, rawDataBlob, destinationBlob);
                    //*****TODO***** should this be using the start copy + delete if exists or the async versions in Engine.
                    //destinationBlob.StartCopy(rawDataBlob);
                    //*****TODO***** should this test validate the transfer to both directories?  Probably.

                } //end if not cloud blobk blob
            } //end loop through all blobs in pending supervision container
        }
    }
}
