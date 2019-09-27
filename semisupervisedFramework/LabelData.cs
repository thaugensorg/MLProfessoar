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
            JObject labelingOutput = JObject.Parse(labelingOutputJsonBlob.DownloadText());

            // Hydrate the raw data file
            string boundJsonFileName = (string)labelingOutput.SelectToken("asset.name");
            string pendingSupervisionStorageContainerName = engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", log);
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            CloudBlockBlob rawDataBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(boundJsonFileName);

            // instanciate bound json blob and get its Json.
            // You must 'touch' the blob before you can access properties or they are all null.
            if (rawDataBlob.Exists())
            {
            }
            JsonBlob boundJsonBlob = new JsonBlob(rawDataBlob.Properties.ContentMD5.ToString(), engine, search, log);
            JProperty labels = (JProperty)boundJsonBlob.Json.SelectToken("labels");
            if (labels == null)
            {
                // Create a labels property, add it to the bound json and then upload the file.
                labels = new JProperty("labels", labelingOutput);
                boundJsonBlob.Json.Add(labels);
            }
            else
            {
                // Update labeling value in bound json to the latest labeling value
                log.LogInformation($"\nJson blob {boundJsonBlob.Name} for  data file {rawDataBlob.Name} already has configured labels {labels}.  Existing labels overwritten.");
                labels["labels"] = labelingOutput;

            }
            // update bound json blob with the latest json
            await engine.UploadJsonBlob(boundJsonBlob.AzureBlob, boundJsonBlob.Json);
        }
    }
}
