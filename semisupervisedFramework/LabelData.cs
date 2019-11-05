using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    //*****TODO***** need to update this so that it is using base classes and subclasses
    class LabelData
    {
        public async void VottLabelData(string labelingJsonBlobName, ILogger log)
        {
            Engine engine = new Engine(log);

            log.LogInformation($"\nInitiating labeling of: {labelingJsonBlobName}");

            Search search = new Search(engine, log);
            Model model = new Model(engine, search, log);

            // Hydrate the labeling results blob
            CloudBlobClient blobClient = engine.StorageAccount.CreateCloudBlobClient();
            string labelingOutputStorageContainerName = engine.GetEnvironmentVariable("labelingOutputStorageContainerName", log);
            CloudBlobContainer labelingOutputStorageContainer = blobClient.GetContainerReference(labelingOutputStorageContainerName);
            CloudBlockBlob labelingOutputJsonBlob = labelingOutputStorageContainer.GetBlockBlobReference(labelingJsonBlobName);

            // Download the labeling results Json
            string labelingOutput = labelingOutputJsonBlob.DownloadText();
            JObject labelingOutputJobject = JObject.Parse(labelingOutput);

            // Hydrate the raw data file
            //*****TODO*****externalize the json location of the file name or make sure it will be in the standardised location created by MLProfessoar.
            string labelingOutputJsonFileName = (string)labelingOutputJobject.SelectToken("asset.name");
            string pendingSupervisionStorageContainerName = engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            CloudBlockBlob rawDataBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(labelingOutputJsonFileName);

            // Hydrate bound json blob and get its Json.
            // You must 'touch' the blob before you can access properties or they are all null.
            if (rawDataBlob.Exists())
            {
            }
            JsonBlob boundJsonBlob = new JsonBlob(rawDataBlob.Properties.ContentMD5.ToString(), engine, search, log);
            JToken labels = (JToken)boundJsonBlob.Json.SelectToken("labels");
            if (labels != null)
            {
                // Update labeling value in bound json to the latest labeling value
                log.LogInformation($"\nJson blob {boundJsonBlob.Name} for  data file {rawDataBlob.Name} already has configured labels {labels}.  Existing labels overwritten.");
                labels.Parent.Remove();
            }

            // Create a labels property, add it to the bound json and then upload the file.
            JProperty labelsJProperty = new JProperty("labels", labelingOutputJobject);
            boundJsonBlob.Json.Add(labelsJProperty);

            // update bound json blob with the latest json
            await engine.UploadJsonBlob(boundJsonBlob.AzureBlob, boundJsonBlob.Json);

            string pendingNewModelStorageContainerName = engine.GetEnvironmentVariable("pendingNewModelStorageContainerName", log);
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);
            string labeledDataStorageContainerName = engine.GetEnvironmentVariable("labeledDataStorageContainerName", log);
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);

            // copy current raw blob working file from pending supervision to labeled data AND pending new model containers
            //*****TODO***** should this be using the start copy + delete if exists or the async versions in Engine.
            //destinationBlob.StartCopy(rawDataBlob);
            CloudBlockBlob labeledDataDestinationBlob = labeledDataStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
            CloudBlockBlob pendingNewModelDestinationBlob = pendingNewModelStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
            await engine.CopyAzureBlobToAzureBlob(engine.StorageAccount, rawDataBlob, pendingNewModelDestinationBlob);
            await engine.MoveAzureBlobToAzureBlob(engine.StorageAccount, rawDataBlob, labeledDataDestinationBlob);
        }
    }
}
