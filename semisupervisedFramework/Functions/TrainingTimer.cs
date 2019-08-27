using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Diagnostics;


using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using semisupervisedFramework.Exceptions;
using semisupervisedFramework.Models;

namespace semisupervisedFramework.Functions
{
    public static class TrainingTimer
    {
        //*****TODO***** Externalize timer frequency.
        [FunctionName("TrainingTimer")]

            //This setting causes the timer job to immediately run when you press F5 rather than having to wait for the timer to fire after n minutes.
#if DEBUG
        public static void Run([TimerTrigger("0 */1 * * * *", RunOnStartup = true)]TimerInfo myTimer, ILogger log)
#else
        public static void Run([TimerTrigger("0 */1 * * * *", RunOnStartup = false)]TimerInfo myTimer, ILogger log)
#endif            
        {
            Engine engine = new Engine(log);
            try
            {
                string responseString = "";
                CloudStorageAccount storageAccount = engine.StorageAccount;
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                //*****TODO***** externalize labeled data container name.
                CloudBlobContainer labeledDataContainer = blobClient.GetContainerReference("labeleddata");
                Model model = new Model(log);

                // with a container load training tags
                if (labeledDataContainer.ListBlobs(null, false) != null)
                {
                    //*****TODO***** Where should search be initialized?  Azure search does not offer CLI calls to configure all of search so it needs to be initialized befor it can be used as a service.  Look at putting it in engine.  Recognize this is not the same thing as migrating search to a non-static mode and then newing it up.
                    //Search.InitializeSearch();

                // Create Reference to Azure Storage Account
                var StorageAccount = Engine.GetStorageAccount(log);
                var BlobClient = StorageAccount.CreateCloudBlobClient();
                var LabeledDataContainer = BlobClient.GetContainerReference("labeleddata");
                var Client = new HttpClient();
                var Response = new HttpResponseMessage();
                var ResponseString = "";

                    //Add full set set of labeled training data to the model
                    //*****TODO***** add logic to only add incremental labeled data to model
                    string addLabeledDataResult = model.AddLabeledData();

                    //Train model using latest labeled training data.
                    string trainingResultsString = model.Train();

                string TrainingDataUrl;
                foreach (var item in LabeledDataContainer.ListBlobs(null, false))
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        var dataCloudBlockBlob = (CloudBlockBlob)item;
                        TrainingDataUrl = dataCloudBlockBlob.Uri.ToString();
                        var BindingHash = dataCloudBlockBlob.Properties.ContentMD5.ToString();
                        if (BindingHash == null)
                        {
                            //compute the file hash as this will be added to the meta data to allow for file version validation
                            var BlobMd5 = BaseModel.CalculateMD5Hash(dataCloudBlockBlob.ToString());
                            if (BlobMd5 == null)
                            {
                                log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                            }
                            else
                            {
                                dataCloudBlockBlob.Properties.ContentMD5 = BlobMd5;
                            }

                        }
                        //trim the 2 "equals" off the trailing end of the hash or the http send will fail either using the client or raw http calls.
                        BindingHash = BindingHash.Substring(0, BindingHash.Length - 2);

                        //Get the content from the bound JSON file and instanciate a JsonBlob class then retrieve the labels collection from the Json to add to the image.
                        var boundJson = (JsonModel)Search.GetBlob("json", BindingHash, log);
                        var trainingDataLabels = Uri.EscapeDataString(JsonConvert.SerializeObject(boundJson.Labels));

                        //construct and call model URL then fetch response
                        // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use
                        //{ {environment variable name}}.
                        // So the example load labels function in the sameple model package would look like this:
                        // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                        // The orchestration engine appends the labels json file to the message body.
                        // http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                        var AddLabeledDataUrl = boundJson.SearchInfo.Url;
                        AddLabeledDataUrl = ConstructModelRequestUrl(AddLabeledDataUrl, trainingDataLabels, log);
                        Response = Client.GetAsync(AddLabeledDataUrl).Result;
                        ResponseString = Response.Content.ReadAsStringAsync().Result;
                        if (string.IsNullOrEmpty(ResponseString)) throw new MissingRequiredObjectException($"\nresponseString not generated from URL: {AddLabeledDataUrl}");

                        //the code below is for passing labels and conent as http content and not on the URL string.
                        //Format the Data Labels content
                        //HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(AddLabeledDataUrl));
                        //HttpContent DataLabelsStringContent = new StringContent(trainingDataLabels, Encoding.UTF8, "application/x-www-form-urlencoded");
                        //MultipartFormDataContent LabeledDataContent = new MultipartFormDataContent();
                        //LabeledDataContent.Add(DataLabelsStringContent, "LabeledData");

                        //Format the data cotent
                        //*****TODO***** move to an async architecture
                        //*****TODO***** need to decide if there is value in sending the data as a binary stream in the post or if requireing the model data scienctist to accept URLs is sufficient.  If accessing the data blob with a SAS url requires Azure classes then create a configuration to pass the data as a stream in the post.  If there is then this should be a configurable option.
                        //MemoryStream dataBlobMemStream = new MemoryStream();
                        //dataBlob.DownloadToStream(dataBlobMemStream);
                        //HttpContent LabeledDataHttpContent = new StreamContent(dataBlobMemStream);
                        //LabeledDataContent.Add(LabeledDataContent, "LabeledData");

                        //Make the http call and get a response
                        //string AddLabelingTagsEndpoint = Engine.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);
                        //if (string.IsNullOrEmpty(AddLabelingTagsEndpoint)) throw (new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set"));
                        //string ResponseString = Helper.GetEvaluationResponseString(AddLabelingTagsEndpoint, LabeledDataContent, log);
                        //if (string.IsNullOrEmpty(ResponseString)) throw (new MissingRequiredObject("\nresponseString not generated from URL: " + AddLabelingTagsEndpoint));

                        log.LogInformation($"Successfully added blob: {dataCloudBlockBlob.Name} with labels: {JsonConvert.SerializeObject(boundJson.Labels)}");
                    }
                }
                //Invoke the train model web service call
                var trainModelUrl = Engine.GetEnvironmentVariable("TrainModelServiceEndpoint", log);
                if (string.IsNullOrEmpty(trainModelUrl)) throw new EnvironmentVariableNotSetException("TrainModelServiceEndpoint environment variable not set");
                Client = new HttpClient();
                Response = Client.GetAsync(trainModelUrl).Result;
                ResponseString = Response.Content.ReadAsStringAsync().Result;
                if (string.IsNullOrEmpty(ResponseString)) throw new MissingRequiredObjectException($"\nresponseString not generated from URL: {trainModelUrl}");

                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            }
            catch (Exception e)
            {
                log.LogInformation("\nError processing training timer: ", e.Message);
            }
        }

        private static void LoadTrainingTags(ILogger log, CloudStorageAccount StorageAccount)
        {
            //Construct a blob client to marshall the storage Functionality
            var LabelsBlobClient = StorageAccount.CreateCloudBlobClient();

            //Construct a blob storage container given a name string and a storage account
            var jsonDataContainerName = Engine.GetEnvironmentVariable("jsonStorageContainerName", log);
            if (string.IsNullOrEmpty(jsonDataContainerName)) throw new EnvironmentVariableNotSetException("jsonStorageContainerName environment variable not set");

            var Container = LabelsBlobClient.GetContainerReference(jsonDataContainerName);

            //get the training tags json blob from the container
            var DataTagsBlobName = Engine.GetEnvironmentVariable("dataTagsBlobName", log);
            if (string.IsNullOrEmpty(DataTagsBlobName)) throw new EnvironmentVariableNotSetException("dataTagsBlobName environment variable not set");

            var DataTagsBlob = Container.GetBlockBlobReference(DataTagsBlobName);

            //the blob has to be "touched" or the properties will all be null
            if (DataTagsBlob.Exists() != true)
            {
                log.LogInformation("The labeling tags blob exists");
            };

            //get the environment variable specifying the MD5 hash of the last run tags file
            var LkgDataTagsFileHash = Engine.GetEnvironmentVariable("dataTagsFileHash", log);

            //Check if there is a new version of the tags json file and if so load them into the environment
            if (DataTagsBlob.Properties.ContentMD5 != LkgDataTagsFileHash)
            {
                //format the http call to load labeling tags
                var AddLabelingTagsEndpoint = Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", log);
                if (string.IsNullOrEmpty(AddLabelingTagsEndpoint)) throw new EnvironmentVariableNotSetException("TagsUploadServiceEndpoint environment variable not set");
                var LabelingTagsParamatersName = Engine.GetEnvironmentVariable("tagDataParameterName", log);
                var LabelingTags = DataTagsBlob.DownloadText(Encoding.UTF8);
                HttpContent LabelingTagsContent = new StringContent(LabelingTags);
                var content = new MultipartFormDataContent();
                content.Add(LabelingTagsContent, "LabelsJson");

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                //string DataTagsUrl = DataTagsBlob.Uri.ToString(); //+ dataEvaluatingSas;

                //Make a request to the model service load labeling tags function passing the tags.
                var ResponseString = Helper.GetEvaluationResponseString(AddLabelingTagsEndpoint, content, log);
                if (string.IsNullOrEmpty(ResponseString)) throw new MissingRequiredObjectException("\nresponseString not generated from URL: " + AddLabelingTagsEndpoint);

                //save the hash of this version of the labeling tags file so that we can avoid running load labeling tags if the file has not changed.
                Environment.SetEnvironmentVariable("dataTagsFileHash", DataTagsBlob.Properties.ContentMD5);
                log.LogInformation(ResponseString);
            }
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructTagUploadRequestUrl(string trainingTags, ILogger log)
        {
            try
            {

                //get environment variables used to construct the model request URL
                var TagUploadServiceEndpoint = Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", log);

                if (TagUploadServiceEndpoint == null || TagUploadServiceEndpoint == "")
                {
                    throw new EnvironmentVariableNotSetException("TagsUploadServiceEndpoint environment variable not set");
                }

                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                var StringReplaceStart = 0;
                var StringReplaceEnd = 0;
                do
                {
                    StringReplaceStart = TagUploadServiceEndpoint.IndexOf("{{", StringReplaceEnd);
                    if (StringReplaceStart != -1)
                    {
                        StringReplaceEnd = TagUploadServiceEndpoint.IndexOf("}}", StringReplaceStart);
                        var StringToReplace = TagUploadServiceEndpoint.Substring(StringReplaceStart, StringReplaceEnd - StringReplaceStart);
                        var ReplacementString = Engine.GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), log);
                        TagUploadServiceEndpoint = TagUploadServiceEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                //http://localhost:7071/api/AddLabeledDataClient/?blobUrl=https://semisupervisedstorage.blob.core.windows.net/testimages/hemlock_2.jpg&imageLabels={%22Labels%22:[%22Hemlock%22]}
                var TagDataParameterName = Engine.GetEnvironmentVariable("tagDataParameterName", log);

                var ModelRequestUrl = TagUploadServiceEndpoint;
                if (TagDataParameterName != null & TagDataParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + TagDataParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + trainingTags;
                }
                else
                {
                    throw new EnvironmentVariableNotSetException("tagDataParameterName environment variable not set");
                }

                return ModelRequestUrl;
            }
            catch (EnvironmentVariableNotSetException e)
            {
                log.LogInformation(e.Message);
                return null;
            }
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string trainingDataUrl, string dataTrainingLabels, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                var LabeledDataServiceEndpoint = Engine.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);
                LabeledDataServiceEndpoint = "https://imagedetectionapp.azurewebsites.net/api/AddLabeledDataClient/";

                if (LabeledDataServiceEndpoint == null || LabeledDataServiceEndpoint == "")
                {
                    throw new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set");
                }

                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                var StringReplaceStart = 0;
                var StringReplaceEnd = 0;
                do
                {
                    StringReplaceStart = LabeledDataServiceEndpoint.IndexOf("{{", StringReplaceEnd);
                    if (StringReplaceStart != -1)
                    {
                        StringReplaceEnd = LabeledDataServiceEndpoint.IndexOf("}}", StringReplaceStart);
                        var StringToReplace = LabeledDataServiceEndpoint.Substring(StringReplaceStart, StringReplaceEnd - StringReplaceStart);
                        var ReplacementString = Engine.GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), log);
                        LabeledDataServiceEndpoint = LabeledDataServiceEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                //http://localhost:7071/api/AddLabeledDataClient/?blobUrl=https://semisupervisedstorage.blob.core.windows.net/testimages/hemlock_2.jpg&imageLabels={%22Labels%22:[%22Hemlock%22]}
                var ModelAssetParameterName = Engine.GetEnvironmentVariable("modelAssetParameterName", log);
                ModelAssetParameterName = "blobUrl";

                var ModelRequestUrl = LabeledDataServiceEndpoint;
                if (ModelAssetParameterName != null & ModelAssetParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + ModelAssetParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + trainingDataUrl;
                    ModelRequestUrl = ModelRequestUrl + "&imageLabels=" + dataTrainingLabels;
                }
                else
                {
                    throw new EnvironmentVariableNotSetException("modelAssetParameterName environment variable not set");
                }

                return ModelRequestUrl;
            }
            catch (EnvironmentVariableNotSetException e)
            {
                log.LogInformation(e.Message);
                return null;
            }
        }
    }
}
