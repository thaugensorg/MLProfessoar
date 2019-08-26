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
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    public class Startup : IExtensionConfigProvider
    {
        // *****todo***** why doesn't this run???
        public void Initialize(ExtensionConfigContext context) => Search.InitializeSearch();
    }
    public static class TrainingTimer
    {
        [FunctionName("TrainingTimer")]
        public static void Run(
                [TimerTrigger("0 */1 * * * *"

#if DEBUG
            , RunOnStartup = true
#endif            

            )]TimerInfo myTimer, ILogger log)
        {
            try
            {

                //Search.InitializeSearch();

                // Create Reference to Azure Storage Account
                CloudStorageAccount StorageAccount = Engine.GetStorageAccount(log);
                CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
                CloudBlobContainer LabeledDataContainer = BlobClient.GetContainerReference("labeleddata");

                // with a container load training tags
                if (LabeledDataContainer.ListBlobs(null, false) != null)
                {
                    LoadTrainingTags(log, StorageAccount);
                }

                string TrainingDataUrl;
                foreach (IListBlobItem item in LabeledDataContainer.ListBlobs(null, false))
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob dataCloudBlockBlob = (CloudBlockBlob)item;
                        TrainingDataUrl = dataCloudBlockBlob.Uri.ToString();
                        string BindingHash = dataCloudBlockBlob.Properties.ContentMD5.ToString();
                        if (BindingHash == null)
                        {
                            //compute the file hash as this will be added to the meta data to allow for file version validation
                            string BlobMd5 = FrameworkBlob.CalculateMD5Hash(dataCloudBlockBlob.ToString());
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
                        JsonBlob boundJson = (JsonBlob)Search.GetBlob("json", BindingHash, log);
                        string trainingDataLabels = Uri.EscapeDataString(JsonConvert.SerializeObject(boundJson.Labels));

                        //construct and call model URL then fetch response
                        // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use
                        //{ {environment variable name}}.
                        // So the example load labels function in the sameple model package would look like this:
                        // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                        // The orchestration engine appends the labels json file to the message body.
                        // http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                        HttpClient Client = new HttpClient();
                        string AddLabeledDataUrl = boundJson.BlobInfo.Url;
                        AddLabeledDataUrl = ConstructModelRequestUrl(AddLabeledDataUrl, trainingDataLabels, log);
                        HttpResponseMessage Response = Client.GetAsync(AddLabeledDataUrl).Result;
                        string ResponseString = Response.Content.ReadAsStringAsync().Result;
                        if (string.IsNullOrEmpty(ResponseString)) throw (new MissingRequiredObject($"\nresponseString not generated from URL: {AddLabeledDataUrl}"));

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
            CloudBlobClient LabelsBlobClient = StorageAccount.CreateCloudBlobClient();

            //Construct a blob storage container given a name string and a storage account
            string jsonDataContainerName = Engine.GetEnvironmentVariable("jsonStorageContainerName", log);
            if (string.IsNullOrEmpty(jsonDataContainerName)) throw (new EnvironmentVariableNotSetException("jsonStorageContainerName environment variable not set"));
        
            CloudBlobContainer Container = LabelsBlobClient.GetContainerReference(jsonDataContainerName);

            //get the training tags json blob from the container
            string DataTagsBlobName = Engine.GetEnvironmentVariable("dataTagsBlobName", log);
            if (string.IsNullOrEmpty(DataTagsBlobName)) throw (new EnvironmentVariableNotSetException("dataTagsBlobName environment variable not set"));
            
            CloudBlockBlob DataTagsBlob = Container.GetBlockBlobReference(DataTagsBlobName);

            //the blob has to be "touched" or the properties will all be null
            if (DataTagsBlob.Exists() != true)
            {
                log.LogInformation("The labeling tags blob exists");
            };

            //get the environment variable specifying the MD5 hash of the last run tags file
            string LkgDataTagsFileHash = Engine.GetEnvironmentVariable("dataTagsFileHash", log);

            //Check if there is a new version of the tags json file and if so load them into the environment
            if (DataTagsBlob.Properties.ContentMD5 != LkgDataTagsFileHash)
            {
                //format the http call to load labeling tags
                string AddLabelingTagsEndpoint = Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", log);
                if (string.IsNullOrEmpty(AddLabelingTagsEndpoint)) throw (new EnvironmentVariableNotSetException("TagsUploadServiceEndpoint environment variable not set"));
                string LabelingTagsParamatersName = Engine.GetEnvironmentVariable("tagDataParameterName", log);
                string LabelingTags = DataTagsBlob.DownloadText(Encoding.UTF8);
                HttpContent LabelingTagsContent = new StringContent(LabelingTags);
                var content = new MultipartFormDataContent();
                content.Add(LabelingTagsContent, "LabelsJson");

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                //string DataTagsUrl = DataTagsBlob.Uri.ToString(); //+ dataEvaluatingSas;

                //Make a request to the model service load labeling tags function passing the tags.
                string ResponseString = Helper.GetEvaluationResponseString(AddLabelingTagsEndpoint, content, log);
                if (string.IsNullOrEmpty(ResponseString)) throw (new MissingRequiredObject("\nresponseString not generated from URL: " + AddLabelingTagsEndpoint));
                
                //save the hash of this version of the labeling tags file so that we can avoid running load labeling tags if the file has not changed.
                System.Environment.SetEnvironmentVariable("dataTagsFileHash", DataTagsBlob.Properties.ContentMD5);
                log.LogInformation(ResponseString);
            }
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructTagUploadRequestUrl(string trainingTags, ILogger log)
        {
            try
            {

                //get environment variables used to construct the model request URL
                string TagUploadServiceEndpoint = Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", log);

                if (TagUploadServiceEndpoint == null || TagUploadServiceEndpoint == "")
                {
                    throw (new EnvironmentVariableNotSetException("TagsUploadServiceEndpoint environment variable not set"));
                }

                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                int StringReplaceStart = 0;
                int StringReplaceEnd = 0;
                do
                {
                    StringReplaceStart = TagUploadServiceEndpoint.IndexOf("{{", StringReplaceEnd);
                    if (StringReplaceStart != -1)
                    {
                        StringReplaceEnd = TagUploadServiceEndpoint.IndexOf("}}", StringReplaceStart);
                        string StringToReplace = TagUploadServiceEndpoint.Substring(StringReplaceStart, StringReplaceEnd - StringReplaceStart);
                        string ReplacementString = Engine.GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), log);
                        TagUploadServiceEndpoint = TagUploadServiceEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                //http://localhost:7071/api/AddLabeledDataClient/?blobUrl=https://semisupervisedstorage.blob.core.windows.net/testimages/hemlock_2.jpg&imageLabels={%22Labels%22:[%22Hemlock%22]}
                string TagDataParameterName = Engine.GetEnvironmentVariable("tagDataParameterName", log);

                string ModelRequestUrl = TagUploadServiceEndpoint;
                if (TagDataParameterName != null & TagDataParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + TagDataParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + trainingTags;
                }
                else
                {
                    throw (new EnvironmentVariableNotSetException("tagDataParameterName environment variable not set"));
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
                string LabeledDataServiceEndpoint = Engine.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);
                LabeledDataServiceEndpoint = "https://imagedetectionapp.azurewebsites.net/api/AddLabeledDataClient/";

                if (LabeledDataServiceEndpoint == null || LabeledDataServiceEndpoint == "")
                {
                    throw (new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set"));
                }

                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                int StringReplaceStart = 0;
                int StringReplaceEnd = 0;
                do
                {
                    StringReplaceStart = LabeledDataServiceEndpoint.IndexOf("{{", StringReplaceEnd);
                    if (StringReplaceStart != -1)
                    {
                        StringReplaceEnd = LabeledDataServiceEndpoint.IndexOf("}}", StringReplaceStart);
                        string StringToReplace = LabeledDataServiceEndpoint.Substring(StringReplaceStart, StringReplaceEnd - StringReplaceStart);
                        string ReplacementString = Engine.GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), log);
                        LabeledDataServiceEndpoint = LabeledDataServiceEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                //http://localhost:7071/api/AddLabeledDataClient/?blobUrl=https://semisupervisedstorage.blob.core.windows.net/testimages/hemlock_2.jpg&imageLabels={%22Labels%22:[%22Hemlock%22]}
                string ModelAssetParameterName = Engine.GetEnvironmentVariable("modelAssetParameterName", log);
                ModelAssetParameterName = "blobUrl";

                string ModelRequestUrl = LabeledDataServiceEndpoint;
                if (ModelAssetParameterName != null & ModelAssetParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + ModelAssetParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + trainingDataUrl;
                    ModelRequestUrl = ModelRequestUrl + "&imageLabels=" + dataTrainingLabels;
                }
                else
                {
                    throw (new EnvironmentVariableNotSetException("modelAssetParameterName environment variable not set"));
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
