using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

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
                //CloudBlobContainer Container = BlobClient.GetContainerReference("labelsjson");
                CloudBlockBlob JsonLabelsBlob = AzureStorage.GetBlob(StorageAccount, "labelsjson", "labels.json", log);

                string LabelsJson = JsonLabelsBlob.DownloadTextAsync().ToString();
                List<string> Labels = JsonConvert.DeserializeObject<List<string>>(LabelsJson);

                //construct and call model URL then fetch response
                HttpClient Client = new HttpClient();
                string AddLabelsURL = "";
                HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(AddLabelsURL));
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

        }
    }
}
