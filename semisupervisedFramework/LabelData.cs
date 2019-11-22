using System.Threading.Tasks;

using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    //*****TODO***** need to update this so that it is using base classes and subclasses
    public abstract class DataLabeler
    {
        virtual public Engine Engine { get; set; }
        public Search Search { get; set; }
        public Model Model { get; set; }

        public DataLabeler(Engine engine, Search search, Model model)
        {
            Engine = engine;
            Search = search;
            Model = model;
        }

        public abstract Task<string> LabelData(string labelingJsonBlobName);
    }

    class VottDataLabeler : DataLabeler
    {
        public VottDataLabeler(Engine engine, Search search, Model model) : base(engine, search, model)
        {
        }

        public override async Task<string> LabelData(string labelingJsonBlobName)
        {
            Engine.Log.LogInformation($"\nInitiating labeling of: {labelingJsonBlobName}");

            // Hydrate the labeling results blob
            CloudBlobClient blobClient = Engine.StorageAccount.CreateCloudBlobClient();
            string labelingOutputStorageContainerName = Engine.GetEnvironmentVariable("labelingOutputStorageContainerName");
            CloudBlobContainer labelingOutputStorageContainer = blobClient.GetContainerReference(labelingOutputStorageContainerName);
            CloudBlockBlob labelingOutputJsonBlob = labelingOutputStorageContainer.GetBlockBlobReference(labelingJsonBlobName);

            // Download the labeling results Json
            string labelingOutput = labelingOutputJsonBlob.DownloadText();
            JObject labelingOutputJobject = JObject.Parse(labelingOutput);

            // Hydrate the raw data file
            //*****TODO*****externalize the json location of the file name or make sure it will be in the standardised location created by MLProfessoar.
            string labelingOutputJsonFileName = (string)labelingOutputJobject.SelectToken("asset.name");
            string pendingSupervisionStorageContainerName = Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName");
            CloudBlobContainer pendingSupervisionStorageContainer = blobClient.GetContainerReference(pendingSupervisionStorageContainerName);
            CloudBlockBlob rawDataBlob = pendingSupervisionStorageContainer.GetBlockBlobReference(labelingOutputJsonFileName);

            // Hydrate bound json blob and get its Json.
            // You must 'touch' the blob before you can access properties or they are all null.
            if (rawDataBlob.Exists())
            {
            }

            // Update labeling value in bound json to the latest labeling value
            JsonBlob boundJsonBlob = new JsonBlob(rawDataBlob.Properties.ContentMD5.ToString(), Engine, Search);
            JToken labels = (JToken)boundJsonBlob.Json.SelectToken("labels");
            if (labels != null)
            {
                // simply delete the bound jason labeling values and re-add the json property.
                Engine.Log.LogInformation($"\nJson blob {boundJsonBlob.Name} for  data file {rawDataBlob.Name} already has configured labels {labels}.  Existing labels overwritten.");
                labels.Parent.Remove();
            }

            // Create a labels property, add it to the bound json and then upload the file.
            JProperty labelsJProperty = new JProperty("labels", labelingOutputJobject);
            boundJsonBlob.Json.Add(labelsJProperty);

            // update bound json blob with the latest json
            await Engine.UploadJsonBlob(boundJsonBlob.AzureBlob, boundJsonBlob.Json);

            string pendingNewModelStorageContainerName = Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName");
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);
            string labeledDataStorageContainerName = Engine.GetEnvironmentVariable("labeledDataStorageContainerName");
            CloudBlobContainer labeledDataStorageContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);

            // copy current raw blob working file from pending supervision to labeled data AND pending new model containers
            //*****TODO***** should this be using the start copy + delete if exists or the async versions in Engine.
            //destinationBlob.StartCopy(rawDataBlob);
            CloudBlockBlob labeledDataDestinationBlob = labeledDataStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
            CloudBlockBlob pendingNewModelDestinationBlob = pendingNewModelStorageContainer.GetBlockBlobReference(rawDataBlob.Name);
            await Engine.CopyAzureBlobToAzureBlob(Engine.StorageAccount, rawDataBlob, pendingNewModelDestinationBlob);
            await Engine.MoveAzureBlobToAzureBlob(Engine.StorageAccount, rawDataBlob, labeledDataDestinationBlob);

            Engine.Log.LogInformation($"\nCompleted labeling of: {labelingJsonBlobName}");

            return $"Labeling of: {labelingJsonBlobName} was successfull.";
        }

    }
    public abstract class DataLabelerFactory
    {
        public abstract DataLabeler GetDataLabeler();
    }

    class VottDataLabelerFactory : DataLabelerFactory
    {
        private Engine _engine;
        private Search _search;
        private Model _model;

        public VottDataLabelerFactory(Engine engine, Search search, Model model)
        {
            _engine = engine;
            _search = search;
            _model = model;
        }

        public override DataLabeler GetDataLabeler()
        {
            return new VottDataLabeler(_engine, _search, _model);
        }
    }
}
