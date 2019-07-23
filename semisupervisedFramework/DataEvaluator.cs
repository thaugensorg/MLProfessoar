using System;
using System.Configuration;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.Azure.Management.CognitiveServices.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    public static class DataEvaluator
    {
        [FunctionName("EvaluateData")]

        public static async Task RunAsync([BlobTrigger("pendingevaluation/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {

            try
            {
                string pendingEvaluationStorageContainerName = "pendingevaluation";
                string evaluatedDataStorageContainerName = "evaluateddata";
                string pendingSupervisionStorageContainerName = "pendingsupervision";
                string modelValidationStorageContainerName = "modelvalidation";
                string pendingNewModelStorageContainerName = "pendingnewmodelevaluation";
                string storageConnection = GetEnvironmentVariable("AzureWebJobsStorage", log);
                string subscriptionKey = GetEnvironmentVariable("CognitiveServicesKey", log);
                string confidenceJSONPath = Environment.GetEnvironmentVariable("confidenceJSONPath");
                double confidenceThreshold = Convert.ToDouble(Environment.GetEnvironmentVariable("confidenceThreshold"));
                double modelVerificationPercent = Convert.ToDouble(Environment.GetEnvironmentVariable("modelVerificationPercentage"));


                // Create Reference to Azure Storage Account
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference("pendingevaluation");

                //compute the file hash as this will be added to the meta data to allow for file version validation
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
                MemoryStream memStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(memStream);
                SHA1Managed sha = new SHA1Managed();
                byte[] checksum = sha.ComputeHash(memStream);

                //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
                CloudBlockBlob dataEvaluating = GetBlob(storageAccount, pendingEvaluationStorageContainerName, blobName);

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                string dataEvaluatingUrl = dataEvaluating.Uri.ToString(); //+ dataEvaluatingSas;
                                                                          //string dataEvaluatingUrl = "test";

                //Make a request to the model service passing the file URL
                string responseString = GetEvaluationResponseString(dataEvaluatingUrl);

                //deserialize response JSON, get confidence score and compare with confidence threshold
                JObject o = JObject.Parse(responseString);
                double confidence = (double)o.SelectToken(confidenceJSONPath);
                //model successfully analyzed content
                if (confidence >= confidenceThreshold)
                {
                    //****still need to attach JSON to blob somehow*****
                    CloudBlockBlob evaluatedData = GetBlob(storageAccount, evaluatedDataStorageContainerName, blobName);
                    TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, evaluatedData, log).Wait();
                    Random rnd = new Random();
                    if (rnd.Next(100) / 100 <= modelVerificationPercent)
                    {
                        //****this is going to fail because the block above will have moved the blob.
                        CloudBlockBlob modelValidation = GetBlob(storageAccount, modelValidationStorageContainerName, blobName);
                        TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, modelValidation, log).Wait();
                    }
                }
                //model was not sufficiently confident in its analysis
                else
                {
                    CloudBlockBlob pendingSupervision = GetBlob(storageAccount, pendingSupervisionStorageContainerName, blobName);
                    TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, pendingSupervision, log).Wait();
                }

                //get the JSON object that comes before the file JSON object
                JObject description = (JObject)o["description"];

                //create the file JSON object
                dynamic fileObject = new JObject();
                fileObject.Name = blobName;
                fileObject.fileID = Guid.NewGuid().ToString();
                fileObject.fileHash = checksum;

                //add the file JSON object after the description JSON object
                //o.Add(fileObject);
                //description.AddAfterSelf(fileObject);

                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
            }
            catch
            {

            }
            finally
            {

            }
        }

        private static string GetEvaluationResponseString(string dataEvaluatingUrl)
        {
            try
            {
                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://branddetectionapp.azurewebsites.net/api/detectBrand/?name=" + dataEvaluatingUrl));
                HttpResponseMessage response = client.SendAsync(request).Result;
                var responseString = response.Content.ReadAsStringAsync().Result;
                return responseString;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public static string GetMD5HashFromBlob(string fileName)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(fileName))
                {
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty);
                }
            }
        }

        public static string GetEnvironmentVariable(string name, ILogger log)
        {
            try
            {
                return System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            }
            
            catch (Exception e)
            {
                log.LogInformation("\nNo environment variable " + name + " in application environment variables", e.Message);
                return "";
            }
        }

        public static async Task TransferAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob, ILogger log)
        {
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint, log);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            try
            {
                task = TransferManager.CopyAsync(sourceBlob, destinationBlob, true, null, context, cancellationSource.Token);
                await task;
            }
            catch (Exception e)
            {
                log.LogInformation("\nThe transfer is canceled: {0}", e.Message);
            }

            stopWatch.Stop();
            log.LogInformation("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
        }

        //Gets a reference to a specific blob using container and blob names as strings
        public static CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            return blob;
        }

        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint, ILogger log)
        {
            SingleTransferContext context = new SingleTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                log.LogInformation("\rBytes transferred: {0}", progress.BytesTransferred);
            });

            return context;
        }

        public static string GetBlobSharedAccessSignature(CloudBlockBlob cloudBlockBlob)
        {
            string sasContainerToken;

            SharedAccessBlobPolicy sharedPolicy = new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTime.UtcNow.AddHours(1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read
            };

            sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(sharedPolicy);
            return sasContainerToken;
        }
        // Analyze a remote image
        private static async Task AnalyzeRemoteAsync(ComputerVisionClient computerVision, string imageUrl, List<VisualFeatureTypes> features, ILogger log)
        {
            if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                log.LogInformation("\nInvalid remoteImageUrl:\n{0} \n", imageUrl);
                return;
            }

            //Analyze image and display the results in the console
            ImageAnalysis analysis = await computerVision.AnalyzeImageAsync(imageUrl, features);
            DisplayResults(analysis, imageUrl, log);
        }

        // Display the most relevant caption for the image
        private static void DisplayResults(ImageAnalysis analysis, string imageUri, ILogger log)
        {
            log.LogInformation(imageUri);
            if (analysis.Description.Captions.Count != 0)
            {
                log.LogInformation(analysis.Description.Captions[0].Text + "\n");
            }
            else
            {
                log.LogInformation("No description generated.");
            }

        }
    }

        public class Program
    {
        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint, ILogger log)
        {
            SingleTransferContext context = new SingleTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                log.LogInformation("\rBytes transferred: {0}", progress.BytesTransferred);
            });

            return context;
        }

        public static DirectoryTransferContext GetDirectoryTransferContext(TransferCheckpoint checkpoint, ILogger log)
        {
            DirectoryTransferContext context = new DirectoryTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                log.LogInformation("\rBytes transferred: {0}", progress.BytesTransferred);
            });

            return context;
        }

        public static void SetNumberOfParallelOperations(ILogger log)
        {
            //Note, the default number of cores applied to parrallel transfer of data is 8, also the default value for the environment variable.  If you change the environment
            //variable be aware this might overwhelm your network and adversely affect other applications on the network.
            string parallelOperations = Environment.GetEnvironmentVariable("parallelOperations");
            log.LogInformation("\nConfiguring " + parallelOperations + " from the parallelOperations environment variable");
            TransferManager.Configurations.ParallelOperations = int.Parse(parallelOperations);
        }

        public static CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName, ILogger log)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            return blob;
        }

        public static CloudBlobDirectory GetBlobDirectory(CloudStorageAccount account, string containerName, ILogger log)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            CloudBlobDirectory blobDirectory = container.GetDirectoryReference("");

            return blobDirectory;
        }

        public static async Task TransferUrlToAzureBlob(CloudStorageAccount account, string sourceURL, CloudBlockBlob destinationBlob, ILogger log)
        {
            Uri uri = new Uri(sourceURL);
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint, log);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            try
            {
                task = TransferManager.CopyAsync(uri, destinationBlob, true, null, context, cancellationSource.Token);
                await task;
            }
            catch (Exception e)
            {
                log.LogInformation("\nThe transfer is canceled: {0}", e.Message);
            }

            stopWatch.Stop();
            log.LogInformation("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
        }
    }
}
