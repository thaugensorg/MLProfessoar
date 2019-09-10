using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;


namespace semisupervisedFramework
{
    class Test
    {
        private ILogger _Log;
        private Engine _Engine;

        public Test(Engine engine, ILogger log)
        {
            _Engine = engine;
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
                if (item is CloudBlockBlob)
                {
                    CloudBlockBlob sourceBlob = (CloudBlockBlob)item;
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
    }
}
