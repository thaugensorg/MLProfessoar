using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    public static class TrainingTimer
    {
        [FunctionName("TrainingTimer")]
        public static void Run([TimerTrigger("0 */1 * * * *")]TimerInfo myTimer, ILogger log)
        {
            try
            {
                // Create Reference to Azure Storage Account
                CloudStorageAccount StorageAccount = Environment.GetStorageAccount(log);
                CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
                CloudBlobContainer PendingEvaluationContainer = BlobClient.GetContainerReference("pendingevaluation");
                string TargetBlobUrl;
                foreach (IListBlobItem item in PendingEvaluationContainer.ListBlobs(null, false))
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;
                        TargetBlobUrl = blob.Uri.ToString();
                    }
                }

                string LabelsJson = JsonLabelsBlob.DownloadTextAsync().ToString();
                // List<string> Labels = JsonConvert.DeserializeObject<List<string>>(LabelsJson);
                JObject LabelsObject = JObject.Parse(LabelsJson);
                string Labels = (string)LabelsObject.SelectToken("Labels");

                //construct and call model URL then fetch response
                // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use {{environment variable name}}.
                // So the example load labels function in the sameple model package would look like this:
                // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                // The orchestration engin appends the labels json file to the message body.
                //http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                HttpClient Client = new HttpClient();
                string AddLabeledDataUrl = "";
                AddLabeledDataUrl = ConstructModelRequestUrl(DataUrl, LabelsJsonlog);
                HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(AddLabeledDataUrl));
                Request.Content = new StringContent(LabelsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                HttpResponseMessage Response = Client.SendAsync(Request).Result;
                string ResponseString = Response.Content.ReadAsStringAsync().Result;

                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            }
            catch (Exception e)
            {
                log.LogInformation("\nError processing training timer: ", e.Message);
            }
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string dataUrl, string labelsJson, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                string LabeledDataServiceEndpoint = Environment.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);

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

                string ModelRequestUrl = LabeledDataServiceEndpoint;
                //if (ModelAssetParameterName != null & ModelAssetParameterName != "")
                //{
                //    ModelRequestUrl = ModelRequestUrl + "?" + ModelAssetParameterName + "=";
                //    ModelRequestUrl = ModelRequestUrl + dataEvaluatingUrl;
                //}
                //else
                //{
                //    throw (new EnvironmentVariableNotSetException("modelAssetParameterName environment variable not set"));
                //}

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
