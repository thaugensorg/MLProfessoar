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
                CloudStorageAccount StorageAccount = Environment.GetStorageAccount(log);
                CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
                CloudBlobContainer LabeledDataContainer = BlobClient.GetContainerReference("labeleddata");
                string TrainingDataUrl;
                foreach (IListBlobItem item in LabeledDataContainer.ListBlobs(null, false))
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;
                        TrainingDataUrl = blob.Uri.ToString();
                        string BindingHash = blob.Properties.ContentMD5.ToString();
                        BindingHash = BindingHash.Substring(0, BindingHash.Length - 2);
                        if (BindingHash == null)
                        {
                            //compute the file hash as this will be added to the meta data to allow for file version validation
                            string BlobMd5 = Blob.CalculateMD5Hash(blob.ToString());
                            if (BlobMd5 == null)
                            {
                                log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                            }
                            else
                            {
                                blob.Properties.ContentMD5 = BlobMd5;
                            }

                        }
                        Blob BoundJson = Blob.GetBoundJson(BindingHash, log);
                        string DataTrainingLabels = JsonConvert.SerializeObject(BoundJson);

                        // string DataTrainingLabels = JsonLabelsBlob.DownloadTextAsync().ToString();
                        // List<string> Labels = JsonConvert.DeserializeObject<List<string>>(LabelsJson);
                        JObject LabelsJsonObject = JObject.Parse(DataTrainingLabels);
                        JToken LabelsToken = LabelsJsonObject.SelectToken("Labels");
                        string Labels = LabelsToken.ToString();

                        //construct and call model URL then fetch response
                        // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use {{environment variable name}}.
                        // So the example load labels function in the sameple model package would look like this:
                        // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                        // The orchestration engin appends the labels json file to the message body.
                        //http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                        HttpClient Client = new HttpClient();
                        string AddLabeledDataUrl = "";
                        AddLabeledDataUrl = ConstructModelRequestUrl(TrainingDataUrl, DataTrainingLabels, log);
                        HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(AddLabeledDataUrl));
                        Request.Content = new StringContent(DataTrainingLabels, Encoding.UTF8, "application/x-www-form-urlencoded");
                        HttpResponseMessage Response = Client.SendAsync(Request).Result;
                        string ResponseString = Response.Content.ReadAsStringAsync().Result;

                        log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
                    }
                }
            }
            catch (Exception e)
            {
                log.LogInformation("\nError processing training timer: ", e.Message);
            }
        }

        //Returns a response string for a given URL.
        private static string GetEvaluationResponseString(string trainingDataUrl, string dataTrainingLabels, ILogger log)
        {
            //initialize variables
            Stopwatch StopWatch = Stopwatch.StartNew();
            string ResponseString = new string("");
            string ModelRequestUrl = new string("");

            try
            {
                //construct and call model URL then fetch response
                HttpClient Client = new HttpClient();
                ModelRequestUrl = ConstructModelRequestUrl(trainingDataUrl, dataTrainingLabels, log);
                HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(ModelRequestUrl));
                HttpResponseMessage Response = Client.SendAsync(Request).Result;
                ResponseString = Response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception e)
            {
                log.LogInformation("\nFailed HTTP request for URL" + trainingDataUrl + " in application environment variables", e.Message);
                return "";
            }

            //log the http elapsed time
            StopWatch.Stop();
            log.LogInformation("\nHTTP call to " + ModelRequestUrl + " completed in:" + StopWatch.Elapsed.TotalSeconds + " seconds.");
            return ResponseString;
        }


        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string trainingDataUrl, string dataTrainingLabels, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                string LabeledDataServiceEndpoint = Environment.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);
                LabeledDataServiceEndpoint = "https://branddetectionapp.azurewebsites.net/api/AddLabeledDataClient/";

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
                        string ReplacementString = Environment.GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), log);
                        LabeledDataServiceEndpoint = LabeledDataServiceEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                string ModelAssetParameterName = Environment.GetEnvironmentVariable("modelAssetParameterName", log);
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
