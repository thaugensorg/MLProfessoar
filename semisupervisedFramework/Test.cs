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
            string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
            CloudBlobContainer pendingEvaluationContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop over items within the container and move them to pending evaluation container
            foreach (IListBlobItem item in testDataContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob sourceBlob)
                {
                    CloudBlockBlob destinationBlob = pendingEvaluationContainer.GetBlockBlobReference(sourceBlob.Name);
                    destinationBlob.StartCopy(sourceBlob);
                }
            }

            int numberOfPasses = 0;
            int countOfBlobs = 0;
            do
            {
                numberOfPasses++;
                countOfBlobs = 0;
                foreach (IListBlobItem item in pendingEvaluationContainer.ListBlobs(null, false))
                {
                    countOfBlobs++;
                    await Task.Delay(1000);
                }
                _Log.LogInformation($"\nOn pass {numberOfPasses}, {countOfBlobs} blobs were found in pending evaluation container {pendingEvaluationStorageContainerName}.");
            } while (countOfBlobs > 0 || numberOfPasses > 10);

            //check that all items have been moved to the pending supervision container
            string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            int verifiedBlobs = 0;
            int checkLoops = 0;

            do
            {
                verifiedBlobs = 0;
                foreach (IListBlobItem item in testDataContainer.ListBlobs(null, false))
                {
                    if (item is CloudBlockBlob verificationBlob)
                    {
                        CloudBlockBlob ExpectedBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(verificationBlob.Name);
                        if (ExpectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 20)
                            {
                                return $"Passed: 20 blobs verified in {pendingSupervisionStorageContainer}";
                            }
                        }

                        await Task.Delay(500);

                    }
                }

                await Task.Delay(1000);
                checkLoops++;

            } while (verifiedBlobs <= 20 && checkLoops <= 10);

            return $"Failed: NoTrainedModelTest only found {verifiedBlobs} in {pendingSupervisionStorageContainer} but 20 were expected.";
        }

        public async Task<string> TrainModelTest()
        {
            // Get a azure storage client
            string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            //get references to the test label data and the location the test blobs needs to be to run tests.
            string jsonStorageContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
            CloudBlobContainer jsonStorageContainer = blobClient.GetContainerReference(jsonStorageContainerName);
            CloudBlockBlob dataLabelingTagsBlob = jsonStorageContainer.GetBlockBlobReference("LabelingTags.json");
            string testDataContainerName = "testdata/testjson";
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference(testDataContainerName);
            CloudBlockBlob testDataLabelingTagsBlob = testDataContainer.GetBlockBlobReference("LabelingTags.json");

            //copy the test labeling tags blob to the expected location.
            dataLabelingTagsBlob.StartCopy(testDataLabelingTagsBlob);

            // mock the supervision loop by labeling data using the blob name hemlock or japenese cherry.
            LabelData();

            // Train the model using the core training process
            _Model.TrainingProcess();

            //*****TODO*****Verify labels were loaded by calling python model

            //Verify labeled data was loaded by calling python model

            //Verify the model was trained and an iteration assigned by calling python model

            return "Passed: Model trained.";
        }

        public async Task<string> EvaluatePassingDataTest()
        {
            // Establish a storage connection
            string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            CloudBlobDirectory passingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("passing");
            CloudBlobDirectory failingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("failing");
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
                foreach (IListBlobItem item in passingEvaluationTestDataDirectory.ListBlobs())
                {
                    if (item is CloudBlockBlob testDataBlob)
                    {
                        string name = testDataBlob.Name.Split('/')[1];
                        CloudBlockBlob ExpectedBlob = evaluatedDataStorageContainer.GetBlockBlobReference(name);
                        if (ExpectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 2)
                            {
                                response = $"Passed: {verifiedBlobs} passing blobs verified in {evaluatedDataStorageContainer}";
                                break;
                            }
                        }

                        await Task.Delay(500);
                    }
                }

                await Task.Delay(1000);
                checkLoops++;
                if (checkLoops > 4)
                {
                    response = $"Failed: only {verifiedBlobs} found in {evaluatedDataStorageContainerName} when 2 were expected.";
                }

            } while (verifiedBlobs < 2 && checkLoops <= 5);

            do
            {
                verifiedBlobs = 0;

                // Loop back through the pass test data container and make sure their is a matching blob in the evaluated data container
                foreach (IListBlobItem item in failingEvaluationTestDataDirectory.ListBlobs())
                {
                    if (item is CloudBlockBlob testDataBlob)
                    {
                        string name = testDataBlob.Name.Split('/')[1];
                        CloudBlockBlob ExpectedBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(name);
                        if (ExpectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 6)
                            {
                                response = response + $"\nPassed: 6 blobs failed evaluation and were verified in {pendingSupervisionStorageContainer}";
                                return response;
                            }
                        }

                        await Task.Delay(500);
                    }
                }

                await Task.Delay(1000);
                checkLoops++;

                if (checkLoops > 5)
                {
                    response = response + $"\nFailed: only {verifiedBlobs} found in {pendingSupervisionStorageContainerName} when 6 were expected.";
                }


            } while (verifiedBlobs < 6 && checkLoops <= 5);

            return "Failed: " + response;

        }

        public async void LabelData()
        {
            // Loop through all files in the pending supervision container and verify the bound json file has a label for the data file.
            string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageConnection);
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

                        string serializedJsonBlob = JsonConvert.SerializeObject(jsonBlobJObject, Formatting.Indented, new JsonSerializerSettings { });
                        Stream jsonBlobMemStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedJsonBlob));
                        if (jsonBlobMemStream.Length != 0)
                        {
                            jsonBlob.AzureBlob.UploadFromStreamAsync(jsonBlobMemStream);
                            _Log.LogInformation($"\nJson blob {jsonBlob.AzureBlob.Name} updated with {labels.ToString()}");
                        }
                        else
                        {
                            throw (new ZeroLengthFileException("\nencoded json memory stream is zero length and cannot be writted to blob storage"));
                        }
                    } //done updating labeling information in json blob

                    // copy current raw blob working file from pending supervision to labeled data AND pending new model containers
                    CloudBlockBlob destinationBlob = labeledDataStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
                    CloudBlockBlob pendingNewModelDestinationBlob = pendingNewModelStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
                    _Engine.CopyAzureBlobToAzureBlob(storageAccount, rawDataBlob, pendingNewModelDestinationBlob);
                    _Engine.MoveAzureBlobToAzureBlob(storageAccount, rawDataBlob, destinationBlob);
                    //*****TODO***** should this be using the start copy + delete if exists or the async versions in Engine.
                    //destinationBlob.StartCopy(rawDataBlob);
                    //*****TODO***** should this test validate the transfer to both directories?  Probably.

                } //end if not cloud blobk blob
            } //end loop through all blobs in pending supervision container
        }
    }
}
