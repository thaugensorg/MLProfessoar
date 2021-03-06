﻿using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

using Newtonsoft.Json.Linq;



namespace semisupervisedFramework
{
    abstract class Test
    {
        public Engine Engine;
        public Search Search;
        public Model Model;

        public Test(Engine engine, Search search, Model model)
        {
            Engine = engine;
            Search = search;
            Model = model;

        }

        // C# await and azync tutorial https://www.youtube.com/watch?v=C5VhaxQWcpE
        public async Task<string> NoTrainedModel()
        {

            // Get a reference to the test data container
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            string pendingEvaluationStorageContainerName = Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName");
            CloudBlobContainer pendingEvaluationContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop over items within the container and move them to pending evaluation container
            await Engine.CopyBlobsFromContainerToContainer(testDataContainer, pendingEvaluationContainer);

            //check that all items have been moved to the pending supervision container
            string pendingSupervisionStorageContainerName = Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName");
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);

            // Get the test file count
            string strTestFileCount = Engine.GetEnvironmentVariable("TestFileCount");

            // Initialize loop control variables
            int verifiedBlobs = 0;
            int checkLoops = 0;
            int testFileCount = 0;

            if (int.TryParse(strTestFileCount, out testFileCount))
            {
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
                                if (verifiedBlobs == testFileCount)
                                {
                                    return $"Passed: {testFileCount} blobs verified in {pendingSupervisionStorageContainerName}";
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
                } while (verifiedBlobs <= testFileCount && checkLoops <= 10);
            }

            return $"Failed: NoTrainedModelTest only found {verifiedBlobs} in {pendingSupervisionStorageContainerName} but {testFileCount} were expected.";
        }

        public async Task<string> LoadLabels()
        {
            // Get a azure storage client
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Get references to the test label data and the location the test blobs needs to be to run tests.
            string jsonStorageContainerName = Engine.GetEnvironmentVariable("jsonStorageContainerName");
            CloudBlobContainer jsonStorageContainer = blobClient.GetContainerReference(jsonStorageContainerName);
            string labelingTagsBlobName = Engine.GetEnvironmentVariable("labelingTagsBlobName");
            CloudBlockBlob dataLabelingTagsBlob = jsonStorageContainer.GetBlockBlobReference(labelingTagsBlobName);
            string testDataContainerName = "testdata";
            CloudBlobContainer testDataContainer = blobClient.GetContainerReference(testDataContainerName);
            CloudBlobDirectory labelingTagsDirectory = testDataContainer.GetDirectoryReference("LabelingTags");
            CloudBlockBlob testDataLabelingTagsBlob = labelingTagsDirectory.GetBlockBlobReference(labelingTagsBlobName);

            //copy the test labeling tags blob to the expected location.
            //*****TODO***** we cannot copy the training lables JSON to the JSON file as it will trigger indexing of the 
            // file which will fail do to mismatched schema.  What we actually need to do is figure out how to retrieve the
            // training tags from the labeling solution such as VoTT.
            //await dataLabelingTagsBlob.StartCopyAsync(testDataLabelingTagsBlob);


            //Load the list of valid training tags to ensure all data labels are valid.
            string loadTrainingTagsResult = Model.LoadTrainingTags();

            return $"\nCompleted loading labeling tags with result {loadTrainingTagsResult}.";
        }

        public async Task<string> TrainModel()
        {

            // Train the model using the core training process
            await Model.TrainingProcess();

            //*****TODO*****Verify labels were loaded by calling python model

            //Verify labeled data was loaded by calling python model

            //Verify the model was trained and an iteration assigned by calling python model

            return "Passed: Model trained.";
        }

        public async Task<string> EvaluateFailingData()
        {
            // Establish a storage connection
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            CloudBlobDirectory failingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("failing");
            string evaluatedDataStorageContainerName = Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName");
            CloudBlobContainer evaluatedDataStorageContainer = blobClient.GetContainerReference(evaluatedDataStorageContainerName);
            string pendingSupervisionStorageContainerName = Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName");
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            string pendingEvaluationStorageContainerName = Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName");
            CloudBlobContainer pendingEvaluationStorageContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop through failing test data and call evaluate by copying blobs to pending evaluation container.
            foreach (IListBlobItem item in failingEvaluationTestDataDirectory.ListBlobs())
            {
                if (item is CloudBlockBlob testDataBlob)
                {
                    string name = testDataBlob.Name.Split('/')[1];
                    CloudBlockBlob evaluateDataBlob = pendingEvaluationStorageContainer.GetBlockBlobReference(name);
                    await Engine.CopyAzureBlobToAzureBlob(storageAccount, testDataBlob, evaluateDataBlob);
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
                            if (verifiedBlobs == 7)
                            {
                                return response + $"\nPassed: 7 blobs did not pass evaluation and were verified in {pendingSupervisionStorageContainerName}";
                            }
                        }

                        await Task.Delay(500);
                    }
                }

                await Task.Delay(1000);
                checkLoops++;

                if (checkLoops > 5)
                {
                    response = response + $"\n{verifiedBlobs} blobs found in {pendingSupervisionStorageContainerName} when 7 were expected.";
                }

            } while (verifiedBlobs < 7 && checkLoops <= 5);

            return "Failed: " + response;

        }

        public async Task<string> EvaluatePassingData()
        {
            // Establish a storage connection
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer testDataContainer = blobClient.GetContainerReference("testdata");
            CloudBlobDirectory passingEvaluationTestDataDirectory = testDataContainer.GetDirectoryReference("passing");
            string evaluatedDataStorageContainerName = Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName");
            CloudBlobContainer evaluatedDataStorageContainer = blobClient.GetContainerReference(evaluatedDataStorageContainerName);
            string pendingSupervisionStorageContainerName = Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName");
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            string pendingEvaluationStorageContainerName = Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName");
            CloudBlobContainer pendingEvaluationStorageContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            // Loop through passing test data and call evaluate by copying blobs to pending evaluation container.
            foreach (IListBlobItem item in passingEvaluationTestDataDirectory.ListBlobs(true))
            {
                if (item is CloudBlockBlob testDataBlob)
                {
                    string name = testDataBlob.Name.Split('/')[1];
                    CloudBlockBlob evaluateDataBlob = pendingEvaluationStorageContainer.GetBlockBlobReference(name);
                    await Engine.CopyAzureBlobToAzureBlob(storageAccount, testDataBlob, evaluateDataBlob);
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
                            if (verifiedBlobs == 2)
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
                    response = $"\n{verifiedBlobs} found in {evaluatedDataStorageContainerName} when 2 were expected.";
                }

            } while (verifiedBlobs < 2 && checkLoops <= 5);

            return "Failed: " + response;

        }

        public abstract Task<string> LabelData();

        public abstract Test Create(string labelingSolutionName);

        public async Task<string> LoadLabeledData()
        {
            // add labeled data to the model.
            string labelingResults = await Model.AddLabeledData();
            return $"\n{labelingResults}";
        }
    }

    class VoTTTestLabeler : Test
    {
        public VoTTTestLabeler(Engine engine, Search search, Model model) : base(engine, search, model)
        {

        }

        public override Test Create(string labelingSolutionName)
        {
            return new VoTTTestLabeler(Engine, Search, Model);
        }

        public override async Task<string> LabelData()
        {
            // Loop through all files in the labeling output container and add the label data to the bound json file.
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Get references to the source container, pending supervision, and the destination container, labeled data
            string testDataContainerName = "testdata";
            CloudBlobContainer vottTestDataContainer = blobClient.GetContainerReference(testDataContainerName);
            string vottTestDataDirectoryName = "votttestjson";
            CloudBlobDirectory vottTestDataDirectory = vottTestDataContainer.GetDirectoryReference(vottTestDataDirectoryName);
            string labeledDataStorageContainerName = Engine.GetEnvironmentVariable("labeledDataStorageContainerName");
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);
            string pendingNewModelStorageContainerName = Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName");
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);
            string labelingOutputStorageContainerName = Engine.GetEnvironmentVariable("labelingOutputStorageContainerName");
            CloudBlobContainer labelingOutputStorageContainer = blobClient.GetContainerReference(labelingOutputStorageContainerName);

            // Mock the supervision loop by labeling data.  This happens by simply copying the test label data into the labelingoutput container
            // The labeling output container has a blob trigger on it that moves labels from the labeler output to the MLProfessoar json file.
            await Engine.CopyBlobsFromDirectoryToContainer(vottTestDataDirectory, labelingOutputStorageContainer);

            // Initialize loop control variables
            int verifiedBlobs = 0;
            int checkLoops = 0;

            // Loop through all blobs in labeled data container and ensure there is a corresponding blob in the expectged pending new model container.
            do
            {
                verifiedBlobs = 0;
                foreach (IListBlobItem item in labeledDataStorageContainer.ListBlobs(null, false))
                {
                    if (item is CloudBlockBlob verificationBlob)
                    {
                        CloudBlockBlob expectedBlob = pendingNewModelStorageContainer.GetBlockBlobReference(verificationBlob.Name);
                        if (expectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 30)
                            {
                                return $"Passed: 30 blobs verified in {labeledDataStorageContainerName} and {pendingNewModelStorageContainer}";
                            }
                        }
                    } //end if not cloud block blob
                } //end loop through labeled data container

                // If after making a pass through all of the test container blobs the test has not passed wait and then check again.  Because the code
                // does not invoke the Orchestration Engine directly we cannot await the call to evaluate data so we have to delay and try again.
                // Given Azure Functions performance we are delaying 5 seconds.  If Azure function performance improves this time can be reduced.
                await Task.Delay(5000);
                checkLoops++;

            // Keep looping until either the test passes or 10 attempts have been made.  *****TODO***** this should be externalized in the future for performance tuning.
            } while (verifiedBlobs <= 30 && checkLoops <= 10);

            return $"failed: {verifiedBlobs} found in {labeledDataStorageContainerName} and {pendingNewModelStorageContainer}, 30 were expected";
        }

    }
    class FileNameTestLabeler : Test
    {
        public FileNameTestLabeler(Engine engine, Search search, Model model) : base(engine, search, model)
        {

        }

        public override Test Create(string labelingSolutionName)
        {
            return new FileNameTestLabeler(Engine, Search, Model);
        }


        public override async Task<string> LabelData()
        {
            // Loop through all files in the pending supervision container and verify the bound json file has a label for the data file.
            string storageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Loop through each blob in the pending supervision container, create labeling json, and upload the file to labeling output to mock
            // a labeling app generating labels and creating a labels output file.
            string pendingSupervisionStorageContainerName = Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName");
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            foreach (IListBlobItem item in pendingSupervisionStorageContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob rawDataBlob)
                {
                    if (rawDataBlob.Exists())
                    {
                        //you have to 'touch' the cloudBlockBlob or the properties will be null.
                    }

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

                    // Create a labels property, add it to the bound json and then upload the file.
                    JObject dataLabelJObject = new JObject(dataLabel);

                    // Create labeling output file using file name as the source of the labels
                    string labelingOutputStorageContainerName = Engine.GetEnvironmentVariable("labelingOutputStorageContainerName");
                    CloudBlobContainer labelingOutputStorageContainer = blobClient.GetContainerReference(labelingOutputStorageContainerName);

                    // Note, we are simply appending the json extention to a full blob name.  This allows downstream code to hydrate the source
                    // raw data blob name.
                    CloudBlockBlob labelingOutputJsonBlob = labelingOutputStorageContainer.GetBlockBlobReference($"{rawDataBlob.Name}.json");
                    labelingOutputJsonBlob.Properties.ContentType = "application/json";
                    await Engine.UploadJsonBlob(labelingOutputJsonBlob, dataLabelJObject);
                    //*****TODO***** should this test validate the transfer to both directories?  Probably.

                } //end if not cloud block blob
            } //end loop through all blobs in pending supervision container

            // verify all blobs are in the correct containers
            // Initialize loop control variables
            int verifiedBlobs = 0;
            int checkLoops = 0;

            // Loop through all blobs in labeled data container and ensure there is a corresponding blob in the expectged pending new model container.
            string pendingNewModelStorageContainerName = Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName");
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);
            string labeledDataStorageContainerName = Engine.GetEnvironmentVariable("labeledDataStorageContainerName");
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);


            do
            {
                verifiedBlobs = 0;
                foreach (IListBlobItem item in labeledDataStorageContainer.ListBlobs(null, false))
                {
                    if (item is CloudBlockBlob verificationBlob)
                    {
                        CloudBlockBlob expectedBlob = pendingNewModelStorageContainer.GetBlockBlobReference(verificationBlob.Name);
                        if (expectedBlob.Exists())
                        {
                            verifiedBlobs++;
                            if (verifiedBlobs == 20)
                            {
                                return $"Passed: 30 blobs verified in {labeledDataStorageContainerName} and {pendingNewModelStorageContainer}";
                            }
                        }
                    } //end if not cloud block blob
                } //end loop through labeled data container

                // If after making a pass through all of the test container blobs the test has not passed wait and then check again.  Because the code
                // does not invoke the Orchestration Engine directly we cannot await the call to evaluate data so we have to delay and try again.
                // Given Azure Functions performance we are delaying 5 seconds.  If Azure function performance improves this time can be reduced.
                await Task.Delay(5000);
                checkLoops++;

            // Keep looping until either the test passes or 10 attempts have been made.  *****TODO***** this should be externalized in the future for performance tuning.
            } while (verifiedBlobs <= 20 && checkLoops <= 10);

            return $"failed: {verifiedBlobs} found in {labeledDataStorageContainerName} and {pendingNewModelStorageContainer}, 20 were expected";
        } //end LabelData method
    } //end FileNameTestLabeler class
}
