using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;


using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class provides the utility functions that enable the orchestration of marshaling the data science
    // model and taking proper action on the data files the model will evaluate.  This is generally enviroenment
    // interaction (think environment variables), http behavior and azure storage marshalling.
    //**********************************************************************************************************

    class Engine
    {
        private ILogger _Log;
        public CloudStorageAccount StorageAccount { get; set; }

        public Engine(ILogger log)
        {
            _Log = log;
            try
            {
                string StorageConnection = GetEnvironmentVariable("AzureWebJobsStorage", _Log);
                StorageAccount = CloudStorageAccount.Parse(StorageConnection);
            }
            catch
            {
                throw;
            }
        }

        //Returns an environment variable matching the name parameter in the current app context
        public string GetEnvironmentVariable(string name, ILogger log)
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

        public JProperty GetEnvironmentJson(ILogger log)
        {
            //create environment JSON object
            //Dont include storage connection as it contains the storage key which should not be placed in storage.
            JProperty BlobEnvironment =
                new JProperty("environment",
                    new JObject(
                        new JProperty("parameter", GetEnvironmentVariable("evaluationDataParameterName", log)),
                        new JProperty("pendingEvaluationStorage", GetEnvironmentVariable("pendingEvaluationStorageContainerName", log)),
                        new JProperty("evaluatedDataStorage", GetEnvironmentVariable("evaluatedDataStorageContainerName", log)),
                        new JProperty("pendingSupervisionStorage", GetEnvironmentVariable("pendingSupervisionStorageContainerName", log)),
                        new JProperty("labeledDataStorage", GetEnvironmentVariable("labeledDataStorageContainerName", log)),
                        new JProperty("modelValidationStorage", GetEnvironmentVariable("modelValidationStorageContainerName", log)),
                        new JProperty("pendingNewModelStorage", GetEnvironmentVariable("pendingNewModelStorageContainerName", log)),
                        new JProperty("confidenceJSONPath", GetEnvironmentVariable("confidenceJSONPath", log)),
                        new JProperty("confidenceThreshold", GetEnvironmentVariable("confidenceThreshold", log)),
                        new JProperty("verificationPercent", GetEnvironmentVariable("modelVerificationPercentage", log))
                    )
                );
            return BlobEnvironment;
        }


        //Builds a URL to call the blob analysis model.
        //******TODO***** need to genericize this so that it works for all requests not just Labeled Data.
        public string ConstructModelRequestUrl(string modelApiEndpoint, string parameters)
        {
            try
            {
                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                int StringReplaceStart = 0;
                int StringReplaceEnd = 0;
                do
                {
                    StringReplaceStart = modelApiEndpoint.IndexOf("{{", StringReplaceEnd);
                    if (StringReplaceStart != -1)
                    {
                        StringReplaceEnd = modelApiEndpoint.IndexOf("}}", StringReplaceStart);
                        string StringToReplace = modelApiEndpoint.Substring(StringReplaceStart, StringReplaceEnd - StringReplaceStart);
                        string ReplacementString = GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), _Log);
                        modelApiEndpoint = modelApiEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (StringReplaceStart != -1);

                //http://localhost:7071/api/AddLabeledDataClient/?blobUrl=https://semisupervisedstorage.blob.core.windows.net/testimages/hemlock_2.jpg&imageLabels={%22Labels%22:[%22Hemlock%22]}

                string ModelRequestUrl = modelApiEndpoint;
                ModelRequestUrl = ModelRequestUrl + parameters;

                return ModelRequestUrl;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        //Returns a response string for a given URL.
        public string GetHttpResponseString(string targetUrl, MultipartFormDataContent postData)
        {
            //initialize variables
            Stopwatch StopWatch = Stopwatch.StartNew();
            string ResponseString = new string("");

            try
            {
                //construct and call model URL then fetch response
                HttpClient Client = new HttpClient();
                Uri TargetUri = new Uri(targetUrl);
                HttpResponseMessage Response = Client.PostAsync(TargetUri, postData).Result;
                ResponseString = Response.Content.ReadAsStringAsync().Result;
                if ((ResponseString == null) && (Response.StatusCode.ToString() == "InternalServerError"))
                {
                    ResponseString = new JObject(new JProperty("500 - InternalServerError")).ToString();
                }
            }
            catch (Exception e)
            {
                _Log.LogInformation("\nFailed HTTP request for URL" + targetUrl + " in application environment variables", e.Message);
                if (e.InnerException.Message == "No such host is known")
                {
                    return "404 - " + e.InnerException.Message;
                }
                else
                {
                    return "";
                }
            }

            //log the http elapsed time
            StopWatch.Stop();
            _Log.LogInformation("\nHTTP call to " + targetUrl + " completed in:" + StopWatch.Elapsed.TotalSeconds + " seconds.");
            return ResponseString;
        }

        //Moves a blob between two azure containers.
        public async Task MoveAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob)
        {
            await CopyAzureBlobToAzureBlob(account, sourceBlob, destinationBlob);

            Stopwatch StopWatch = Stopwatch.StartNew();
            await sourceBlob.DeleteIfExistsAsync();
            StopWatch.Stop();
            _Log.LogInformation("The Azure Blob " + sourceBlob + " deleted in: " + StopWatch.Elapsed.TotalSeconds + " seconds.");
        }

        //Copies a blob between two azure containers.
        public async Task CopyAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob)
        {
            TransferCheckpoint Checkpoint = null;
            SingleTransferContext Context = GetSingleTransferContext(Checkpoint);
            CancellationTokenSource CancellationSource = new CancellationTokenSource();

            Stopwatch StopWatch = Stopwatch.StartNew();
            Task Task;
            try
            {
                Task = TransferManager.CopyAsync(sourceBlob, destinationBlob, CopyMethod.ServiceSideSyncCopy, null, Context, CancellationSource.Token);
                await Task;
            }
            catch (AggregateException e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }
            catch (TransferException e)
            {
                _Log.LogInformation("The Azure Blob " + sourceBlob + " already exists in " + destinationBlob.Parent.Container.Name);
            }
            catch (Exception e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }

            StopWatch.Stop();
            _Log.LogInformation("The Azure Blob " + sourceBlob + " transfer to " + destinationBlob + " completed in:" + StopWatch.Elapsed.TotalSeconds + " seconds.");
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
        public SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint)
        {
            try
            {
                SingleTransferContext Context = new SingleTransferContext(checkpoint);

                Context.ProgressHandler = new Progress<TransferStatus>((Progress) =>
                {
                    _Log.LogInformation("\rBytes transferred: {0}", Progress.BytesTransferred);
                });

                return Context;
            }
            catch (Exception e)
            {
                _Log.LogInformation("\nGet transfer progress update fails.", e.Message);
                return null;
            }
        }

        //Returns a blob shared access signature.
        public string GetBlobSharedAccessSignature(CloudBlockBlob cloudBlockBlob)
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

        public string DecodeBase64String(string encodedString)
        {
            var encodedStringWithoutTrailingCharacter = encodedString.Substring(0, encodedString.Length - 1);
            var encodedBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encodedStringWithoutTrailingCharacter);
            return HttpUtility.UrlDecode(encodedBytes, Encoding.UTF8);
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
