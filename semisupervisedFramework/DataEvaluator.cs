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
                string confidenceJSONPath = GetEnvironmentVariable("confidenceJSONPath", log);
                double confidenceThreshold = Convert.ToDouble(GetEnvironmentVariable("confidenceThreshold", log));
                double modelVerificationPercent = Convert.ToDouble(GetEnvironmentVariable("modelVerificationPercentage", log));


                // Create Reference to Azure Storage Account
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference("pendingevaluation");

                //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
                CloudBlockBlob dataEvaluating = GetBlob(storageAccount, pendingEvaluationStorageContainerName, blobName, log);
                if (dataEvaluating == null)
                {
                    throw (new MissingRequiredObject("\nMissing dataEvaluating blob object."));
                }

                //compute the file hash as this will be added to the meta data to allow for file version validation
                byte[] checksum = await CalculateBlobHash(dataEvaluating, log);
                if (checksum == null)
                {
                    log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                }

                //create the blob Info JSON object for to join blobs with the correct JSON
                dynamic blobInfoJsonObject = new JObject();
                blobInfoJsonObject.Name = blobName;
                blobInfoJsonObject.blobLastModified = dataEvaluating.Properties.LastModified.ToString();
                blobInfoJsonObject.fileID = Guid.NewGuid().ToString();
                blobInfoJsonObject.fileHash = checksum.ToString();

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                string dataEvaluatingUrl = dataEvaluating.Uri.ToString(); //+ dataEvaluatingSas;
                //string dataEvaluatingUrl = "test";

                //Make a request to the model service passing the file URL
                string responseString = GetEvaluationResponseString(dataEvaluatingUrl, log);
                if (responseString == "")
                {
                    throw (new MissingRequiredObject("\nresponseString not generated from URL: " + dataEvaluatingUrl));
                }

                //deserialize response JSON, get confidence score and compare with confidence threshold
                JObject o = JObject.Parse(responseString);
                string strConfidence = (string)o.SelectToken(confidenceJSONPath);
                double confidence = (double)o.SelectToken(confidenceJSONPath);
                if (strConfidence == null)
                {
                    throw (new MissingRequiredObject("\nNo confidence value at " + confidenceJSONPath + " from environment variable ConfidenceJSONPath."));
                }

                //model successfully analyzed content
                if (confidence >= confidenceThreshold)
                {
                    //****still need to attach JSON to blob somehow*****
                    CloudBlockBlob evaluatedData = GetBlob(storageAccount, evaluatedDataStorageContainerName, blobName, log);
                    if (evaluatedData == null)
                    {
                        throw (new MissingRequiredObject("\nMissing evaluatedData " + blobName + " destination blob in container " + evaluatedDataStorageContainerName));
                    }
                    TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, evaluatedData, log).Wait();

                    //pick a random number of successfully analyzed content blobs and submit them for supervision verification.
                    Random rnd = new Random();
                    if (rnd.Next(100) / 100 <= modelVerificationPercent)
                    {
                        //****this is going to fail because the block above will have moved the blob.
                        CloudBlockBlob modelValidation = GetBlob(storageAccount, modelValidationStorageContainerName, blobName, log);
                        if (modelValidation == null)
                        {
                            log.LogInformation("\nWarning: Model validation skipped for " + blobName + " because of missing evaluatedData " + blobName + " destination blob in container " + modelValidationStorageContainerName);
                        }
                        else
                        {
                            TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, modelValidation, log).Wait();
                        }
                    }
                }

                //model was not sufficiently confident in its analysis
                else
                {
                    CloudBlockBlob pendingSupervision = GetBlob(storageAccount, pendingSupervisionStorageContainerName, blobName, log);
                    if (pendingSupervision == null)
                    {
                        throw (new MissingRequiredObject("\nMissing pendingSupervision " + blobName + " destination blob in container " + pendingSupervisionStorageContainerName));
                    }

                    TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, pendingSupervision, log).Wait();
                }

                //get the JSON object that comes before the file JSON object
                //JObject description = (JObject)o["description"];

                //create environment JSON object
                dynamic environmentJsonObject = new JObject();

                //add the file JSON object after the description JSON object
                o.Add(blobInfoJsonObject);
                //description.AddAfterSelf(fileObject);

                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
            }
            catch (MissingRequiredObject e)
            {
                log.LogInformation("\n" + blobName + "could not be analyzed with message: " + e.Message);
            }
            catch (Exception e)
            {
                log.LogInformation("\n" + blobName + "could not be analyzed with message: " + e.Message);
            }
        }

        //calculates a blob hash to join JSON to a specific version of a file.
        private static async Task<byte[]> CalculateBlobHash(CloudBlockBlob blockBlob, ILogger log)
        {
            try
            {
                MemoryStream memStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(memStream);
                if (memStream.Length == 0)
                {
                    throw (new ZeroLengthFileException("\nCloud Block Blob: " + blockBlob.Name + " is zero length"));
                }
                SHA1Managed sha = new SHA1Managed();
                byte[] checksum = sha.ComputeHash(memStream);
                return checksum;
            }
            catch (ZeroLengthFileException e)
            {
                log.LogInformation("\n" +blockBlob.Name + " is zero length.  CalculateBlobFileHash failed with error: " + e.Message);
                return null;
            }
            catch (Exception e)
            {
                log.LogInformation("\ncalculatingBlobFileHash for " + blockBlob.Name + " failed with: " + e.Message);
                return null;
            }
        }

        //Returns a response string for a given URL.
        private static string GetEvaluationResponseString(string dataEvaluatingUrl, ILogger log)
        {
            //initialize variables
            Stopwatch stopWatch = Stopwatch.StartNew();
            string responseString = new string("");
            string modelRequestUrl = new string("");

            try
            {
                //construct and call model URL then fetch response
                HttpClient client = new HttpClient();
                modelRequestUrl = ConstructModelRequestUrl(dataEvaluatingUrl, log);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(modelRequestUrl));
                //HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://branddetectionapp.azurewebsites.net/api/detectBrand/?name=" + dataEvaluatingUrl));
                HttpResponseMessage response = client.SendAsync(request).Result;
                responseString = response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception e)
            {
                log.LogInformation("\nFailed HTTP request for URL" + dataEvaluatingUrl + " in application environment variables", e.Message);
                return "";
            }

            //log the http elapsed time
            stopWatch.Stop();
            log.LogInformation("\nHTTP call to " + modelRequestUrl + " completed in:" + stopWatch.Elapsed.TotalSeconds + " seconds.");
            return responseString;
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string dataEvaluatingUrl, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                string modelServiceEndpoint = GetEnvironmentVariable("modelServiceEndpoint", log);

                if (modelServiceEndpoint == null || modelServiceEndpoint == "") 
                {
                    throw (new EnvironmentVariableNotSetException("modelServiceEndpoint environment variable not set"));
                }
                string modelAssetParameterName = GetEnvironmentVariable("modelAssetParameterName", log);

                //construct model request URL
                string modelRequestUrl = modelServiceEndpoint;
                if (modelAssetParameterName != null & modelAssetParameterName != "")
                {
                    modelRequestUrl = modelRequestUrl + "?" + modelAssetParameterName + "=";
                    modelRequestUrl = modelRequestUrl + dataEvaluatingUrl;
                }
                else
                {
                    throw (new EnvironmentVariableNotSetException("modelAssetParameterName environment variable not set"));
                }

                return modelRequestUrl;
            }
            catch (EnvironmentVariableNotSetException e)
            {
                log.LogInformation(e.Message);
                return null;
            }
        }

        //Returns an environemtn variable matching the name parameter in the current app context
        public static string GetEnvironmentVariable(string name, ILogger log)
        {
            try
            {
                string environmentVariable = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
                if (environmentVariable == null || environmentVariable == "")
                {
                    throw (new EnvironmentVariableNotSetException("\n" + name + " environment variable not set"));
                }
                else
                {
                    return environmentVariable;
                }
            }
            catch (EnvironmentVariableNotSetException e)
            {
                log.LogInformation("\nNo environment variable " + name + " in application environment variables", e.Message);
                return null;
            }
            catch (Exception e)
            {
                log.LogInformation("\nNo environment variable " + name + " in application environment variables", e.Message);
                return null;
            }
        }

        //Transfers a blob between two azure containers.
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
                log.LogInformation("\nThe Azure Blob " + sourceBlob + " transfer to " + destinationBlob + " failed.", e.Message);
            }

            stopWatch.Stop();
            log.LogInformation("The Azure Blob " + sourceBlob + " transfer to " + destinationBlob + " completed in:" + stopWatch.Elapsed.TotalSeconds + " seconds.");
        }

        //Gets a reference to a specific blob using container and blob names as strings
        public static CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName, ILogger log)
        {
            try
            {
                CloudBlobClient blobClient = account.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference(containerName);
                container.CreateIfNotExistsAsync().Wait();

                CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

                return blob;
            }
            catch (Exception e)
            {
                log.LogInformation("\nNo blob " + blobName + " found in " + containerName + " ", e.Message);
                return null;
            }
        }

        //returns an Azure file transfer context for making a single file transfer.
        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint, ILogger log)
        {
            try
            {
                SingleTransferContext context = new SingleTransferContext(checkpoint);

                context.ProgressHandler = new Progress<TransferStatus>((progress) =>
                {
                    log.LogInformation("\rBytes transferred: {0}", progress.BytesTransferred);
                });

                return context;
            }
            catch (Exception e)
            {
                log.LogInformation("\nGet transfer progress update fails.", e.Message);
                return null;
            }
        }

        //Returns a blob shared access signature.
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

        // Write the response body to the log.
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

    public class EnvironmentVariableNotSetException : Exception
    {
        public EnvironmentVariableNotSetException(string message)
            : base(message)
        {
        }
    }

    public class InvalidUrlException : Exception
    {
        public InvalidUrlException(string message)
            : base(message)
        {
        }
    }

    public class ZeroLengthFileException : Exception
    {
        public ZeroLengthFileException(string message)
            : base(message)
        {
        }
    }

    public class MissingRequiredObject : Exception
    {
        public MissingRequiredObject(string message)
            : base(message)
        {
        }
    }
}
