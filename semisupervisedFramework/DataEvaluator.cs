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
                string PendingEvaluationStorageContainerName = GetEnvironmentVariable("pendingEvaluationStorageContainerName", log);
                string EvaluatedDataStorageContainerName = GetEnvironmentVariable("evaluatedDataStorageContainerName", log);
                string EvaluatedJSONStorageContainerName = GetEnvironmentVariable("evaluatedJSONStorageContainerName", log);
                string PendingSupervisionStorageContainerName = GetEnvironmentVariable("pendingSupervisionStorageContainerName", log);
                string ModelValidationStorageContainerName = GetEnvironmentVariable("modelValidationStorageContainerName", log);
                string PendingNewModelStorageContainerName = GetEnvironmentVariable("pendingNewModelStorageContainerName", log);
                string StorageConnection = GetEnvironmentVariable("AzureWebJobsStorage", log);
                string SubscriptionKey = GetEnvironmentVariable("CognitiveServicesKey", log);
                string ConfidenceJsonPath = GetEnvironmentVariable("confidenceJSONPath", log);
                double ConfidenceThreshold = Convert.ToDouble(GetEnvironmentVariable("confidenceThreshold", log));
                double ModelVerificationPercent = Convert.ToDouble(GetEnvironmentVariable("modelVerificationPercentage", log));

                //------------------------This section retrieves the blob needing evaluation and calls the evaluation service for processing.-----------------------

                // Create Reference to Azure Storage Account
                CloudStorageAccount StorageAccount = CloudStorageAccount.Parse(StorageConnection);
                CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
                CloudBlobContainer Container = BlobClient.GetContainerReference("pendingevaluation");

                //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
                CloudBlockBlob DataEvaluating = GetBlob(StorageAccount, PendingEvaluationStorageContainerName, blobName, log);
                if (DataEvaluating == null)
                {
                    throw (new MissingRequiredObject("\nMissing dataEvaluating blob object."));
                }

                //compute the file hash as this will be added to the meta data to allow for file version validation
                byte[] Checksum = await CalculateBlobHash(DataEvaluating, log);
                if (Checksum == null)
                {
                    log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                }

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                string DataEvaluatingUrl = DataEvaluating.Uri.ToString(); //+ dataEvaluatingSas;
                //string dataEvaluatingUrl = "test";

                //Make a request to the model service passing the file URL
                string ResponseString = GetEvaluationResponseString(DataEvaluatingUrl, log);
                if (ResponseString == "")
                {
                    throw (new MissingRequiredObject("\nresponseString not generated from URL: " + DataEvaluatingUrl));
                }

                //deserialize response JSON, get confidence score and compare with confidence threshold
                JObject AnalysisJson = JObject.Parse(ResponseString);
                string StrConfidence = (string)AnalysisJson.SelectToken(ConfidenceJsonPath);
                double Confidence = (double)AnalysisJson.SelectToken(ConfidenceJsonPath);
                if (StrConfidence == null)
                {
                    throw (new MissingRequiredObject("\nNo confidence value at " + ConfidenceJsonPath + " from environment variable ConfidenceJSONPath."));
                }

                //--------------------------------This section processes the results of the analysis and transferes the blob to the container responsible for the nest appropriate stage of processing.-------------------------------

                //model successfully analyzed content
                if (Confidence >= ConfidenceThreshold)
                {
                    CloudBlockBlob EvaluatedData = GetBlob(StorageAccount, EvaluatedDataStorageContainerName, blobName, log);
                    if (EvaluatedData == null)
                    {
                        throw (new MissingRequiredObject("\nMissing evaluatedData " + blobName + " destination blob in container " + EvaluatedDataStorageContainerName));
                    }
                    CopyAzureBlobToAzureBlob(StorageAccount, DataEvaluating, EvaluatedData, log).Wait();

                    //pick a random number of successfully analyzed content blobs and submit them for supervision verification.
                    Random Rnd = new Random();
                    if (Math.Round(Rnd.NextDouble(),2) <= ModelVerificationPercent)
                    {
                        CloudBlockBlob ModelValidation = GetBlob(StorageAccount, ModelValidationStorageContainerName, blobName, log);
                        if (ModelValidation == null)
                        {
                            log.LogInformation("\nWarning: Model validation skipped for " + blobName + " because of missing evaluatedData " + blobName + " destination blob in container " + ModelValidationStorageContainerName);
                        }
                        else
                        {
                            MoveAzureBlobToAzureBlob(StorageAccount, DataEvaluating, ModelValidation, log).Wait();
                        }
                    }
                    await DataEvaluating.DeleteIfExistsAsync();
                }

                //model was not sufficiently confident in its analysis
                else
                {
                    CloudBlockBlob PendingSupervision = GetBlob(StorageAccount, PendingSupervisionStorageContainerName, blobName, log);
                    if (PendingSupervision == null)
                    {
                        throw (new MissingRequiredObject("\nMissing pendingSupervision " + blobName + " destination blob in container " + PendingSupervisionStorageContainerName));
                    }

                    MoveAzureBlobToAzureBlob(StorageAccount, DataEvaluating, PendingSupervision, log).Wait();
                }

                //----------------------------This section collects information about the blob being analyzied and packages it in JSON that is then written to blob storage for later processing-----------------------------------

                JObject BlobAnalysis =
                    new JObject(
                        new JProperty("blobInfo",
                            new JObject(
                                new JProperty("name", blobName),
                                new JProperty("modified", DataEvaluating.Properties.LastModified.ToString()),
                                new JProperty("id", Guid.NewGuid().ToString()),
                                new JProperty("hash", Checksum.ToString())
                            )
                        )
                    );

                //create environment JSON object
                JProperty BlobEnvironment =
                    new JProperty("environment",
                        new JObject(
                            new JProperty("endpoint", GetEnvironmentVariable("modelServiceEndpoint", log)),
                            new JProperty("parameter", GetEnvironmentVariable("modelAssetParameterName", log)),
                            new JProperty("pendingEvaluationStorage", PendingEvaluationStorageContainerName),
                            new JProperty("evaluatedDataStorage", EvaluatedDataStorageContainerName),
                            new JProperty("pendingSupervisionStorage", PendingSupervisionStorageContainerName),
                            new JProperty("modelValidationStorage", ModelValidationStorageContainerName),
                            new JProperty("pendingNewModelStorage", PendingNewModelStorageContainerName),
                            new JProperty("confidenceJSONPath", ConfidenceJsonPath),
                            new JProperty("confidenceThreshold", GetEnvironmentVariable("confidenceThreshold", log)),
                            new JProperty("verificationPercent", GetEnvironmentVariable("modelVerificationPercentage", log))
                        )
                    );

                BlobAnalysis.Add(BlobEnvironment);
                BlobAnalysis.Merge(AnalysisJson);

                CloudBlockBlob JsonBlob = GetBlob(StorageAccount, EvaluatedJSONStorageContainerName, (string)BlobAnalysis.SelectToken("blobInfo.id") + ".json", log);
                JsonBlob.Properties.ContentType = "application/json";
                string SerializedJson = JsonConvert.SerializeObject(BlobAnalysis, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { });
                Stream MemStream = new MemoryStream(Encoding.UTF8.GetBytes(SerializedJson));
                if (MemStream.Length != 0)
                {
                    await JsonBlob.UploadFromStreamAsync(MemStream);
                }
                else
                {
                    throw (new ZeroLengthFileException("\nencoded JSON memory stream is zero length and cannot be writted to blob storage"));
                }


                log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
            }
            catch (MissingRequiredObject e)
            {
                log.LogInformation("\n" + blobName + " could not be analyzed with message: " + e.Message);
            }
            catch (Exception e)
            {
                log.LogInformation("\n" + blobName + " could not be analyzed with message: " + e.Message);
            }
        }

        //calculates a blob hash to join JSON to a specific version of a file.
        private static async Task<byte[]> CalculateBlobHash(CloudBlockBlob blockBlob, ILogger log)
        {
            try
            {
                MemoryStream MemStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(MemStream);
                if (MemStream.Length == 0)
                {
                    throw (new ZeroLengthFileException("\nCloud Block Blob: " + blockBlob.Name + " is zero length"));
                }
                SHA1Managed Sha = new SHA1Managed();
                byte[] Checksum = Sha.ComputeHash(MemStream);
                return Checksum;
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
            Stopwatch StopWatch = Stopwatch.StartNew();
            string ResponseString = new string("");
            string ModelRequestUrl = new string("");

            try
            {
                //construct and call model URL then fetch response
                HttpClient Client = new HttpClient();
                ModelRequestUrl = ConstructModelRequestUrl(dataEvaluatingUrl, log);
                HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(ModelRequestUrl));
                HttpResponseMessage Response = Client.SendAsync(Request).Result;
                ResponseString = Response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception e)
            {
                log.LogInformation("\nFailed HTTP request for URL" + dataEvaluatingUrl + " in application environment variables", e.Message);
                return "";
            }

            //log the http elapsed time
            StopWatch.Stop();
            log.LogInformation("\nHTTP call to " + ModelRequestUrl + " completed in:" + StopWatch.Elapsed.TotalSeconds + " seconds.");
            return ResponseString;
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string dataEvaluatingUrl, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                string ModelServiceEndpoint = GetEnvironmentVariable("modelServiceEndpoint", log);

                if (ModelServiceEndpoint == null || ModelServiceEndpoint == "") 
                {
                    throw (new EnvironmentVariableNotSetException("modelServiceEndpoint environment variable not set"));
                }
                string ModelAssetParameterName = GetEnvironmentVariable("modelAssetParameterName", log);

                //construct model request URL
                string ModelRequestUrl = ModelServiceEndpoint;
                if (ModelAssetParameterName != null & ModelAssetParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + ModelAssetParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + dataEvaluatingUrl;
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

        //Returns an environemtn variable matching the name parameter in the current app context
        public static string GetEnvironmentVariable(string name, ILogger log)
        {
            try
            {
                string EnvironmentVariable = System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
                if (EnvironmentVariable == null || EnvironmentVariable == "")
                {
                    throw (new EnvironmentVariableNotSetException("\n" + name + " environment variable not set"));
                }
                else
                {
                    return EnvironmentVariable;
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

        //Moves a blob between two azure containers.
        public static async Task MoveAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob, ILogger log)
        {
            await CopyAzureBlobToAzureBlob(account, sourceBlob, destinationBlob, log);

            Stopwatch StopWatch = Stopwatch.StartNew();
            await sourceBlob.DeleteIfExistsAsync();
            StopWatch.Stop();
            log.LogInformation("The Azure Blob " + sourceBlob + " deleted in: " + StopWatch.Elapsed.TotalSeconds + " seconds.");
        }


        //Copies a blob between two azure containers.
        public static async Task CopyAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob, ILogger log)
        {
            TransferCheckpoint Checkpoint = null;
            SingleTransferContext Context = GetSingleTransferContext(Checkpoint, log);
            CancellationTokenSource CancellationSource = new CancellationTokenSource();

            Stopwatch StopWatch = Stopwatch.StartNew();
            Task Task;
            try
            {
                Task = TransferManager.CopyAsync(sourceBlob, destinationBlob, true, null, Context, CancellationSource.Token);
                await Task;
            }
            catch (AggregateException e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }
            catch (Exception e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }
            
            StopWatch.Stop();
            log.LogInformation("The Azure Blob " + sourceBlob + " transfer to " + destinationBlob + " completed in:" + StopWatch.Elapsed.TotalSeconds + " seconds.");
        }


        //Gets a reference to a specific blob using container and blob names as strings
        public static CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName, ILogger log)
        {
            try
            {
                CloudBlobClient BlobClient = account.CreateCloudBlobClient();
                CloudBlobContainer Container = BlobClient.GetContainerReference(containerName);
                Container.CreateIfNotExistsAsync().Wait();

                CloudBlockBlob Blob = Container.GetBlockBlobReference(blobName);

                return Blob;
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
                SingleTransferContext Context = new SingleTransferContext(checkpoint);

                Context.ProgressHandler = new Progress<TransferStatus>((Progress) =>
                {
                    log.LogInformation("\rBytes transferred: {0}", Progress.BytesTransferred);
                });

                return Context;
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
            string SasContainerToken;

            SharedAccessBlobPolicy SharedPolicy = new SharedAccessBlobPolicy()
            {
                //******* To Do: change to a more appropriate time than always 1 hour.  Maybe make this configurable.
                SharedAccessStartTime = DateTime.UtcNow.AddHours(1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read
            };

            SasContainerToken = cloudBlockBlob.GetSharedAccessSignature(SharedPolicy);
            return SasContainerToken;
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
