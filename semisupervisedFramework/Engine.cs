using System;
using System.Text;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.IO;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

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

        public JProperty GetEnvironmentJson(ILogger log)
        {
            //create environment JSON object
            //Dont include storage connection as it contains the storage key which should not be placed in storage.
            JProperty blobEnvironment =
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
            return blobEnvironment;
        }


        //Builds a URL to call the blob analysis model.
        //******TODO***** need to genericize this so that it works for all requests not just Labeled Data.
        public string ConstructModelRequestUrl(string modelApiEndpoint, string parameters)
        {
            try
            {
                // *****TODO***** enable string replacement for endpoint URLs.  THis will allow calling functions to be able to controle parameters that are passed.
                // use the following order blob attributes, environment variables, URL parameters.
                int stringReplaceStart = 0;
                int stringReplaceEnd = 0;
                do
                {
                    stringReplaceStart = modelApiEndpoint.IndexOf("{{", stringReplaceEnd);
                    if (stringReplaceStart != -1)
                    {
                        stringReplaceEnd = modelApiEndpoint.IndexOf("}}", stringReplaceStart);
                        string StringToReplace = modelApiEndpoint.Substring(stringReplaceStart, stringReplaceEnd - stringReplaceStart);
                        string ReplacementString = GetEnvironmentVariable(StringToReplace.Substring(2, StringToReplace.Length - 2), _Log);
                        modelApiEndpoint = modelApiEndpoint.Replace(StringToReplace, ReplacementString);
                    }
                } while (stringReplaceStart != -1);

                string modelRequestUrl = modelApiEndpoint;
                modelRequestUrl = modelRequestUrl + parameters;

                return modelRequestUrl;
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
            Stopwatch stopWatch = Stopwatch.StartNew();
            string responseString = new string("");

            try
            {
                //construct and call model URL then fetch response
                HttpClient client = new HttpClient();
                Uri targetUri = new Uri(targetUrl);
                HttpResponseMessage response = client.PostAsync(targetUri, postData).Result;
                if (response.StatusCode.ToString() == "OK")
                {
                    responseString = response.Content.ReadAsStringAsync().Result;
                }
                else
                {
                    if (response.Content.ReadAsStringAsync().Result == "")
                    {
                        responseString = new JObject(new JProperty(response.ReasonPhrase)).ToString();
                    }
                    else
                    {
                        responseString = new JObject(new JProperty(response.Content.ReadAsStringAsync().Result)).ToString();
                    }
                }
            }
            catch (Exception e)
            {
                _Log.LogInformation($"\nFailed HTTP request for URL {targetUrl} in application environment variables with message: {e.Message}");
                if (e.InnerException.Message == "No such host is known")
                {
                    return new JObject(new JProperty("404 - " + e.InnerException.Message)).ToString();
                }
                else
                {
                    return new JObject(new JProperty(e.InnerException.Message)).ToString();
                }
            }

            //log the http elapsed time
            stopWatch.Stop();
            _Log.LogInformation("\nHTTP call to " + targetUrl + " completed in:" + stopWatch.Elapsed.TotalSeconds + " seconds.");
            return responseString;
        }

        //Moves a blob between two azure containers.
        public async Task MoveAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob)
        {
            Stopwatch stopWatch = Stopwatch.StartNew();
            await CopyAzureBlobToAzureBlob(account, sourceBlob, destinationBlob);
            await sourceBlob.DeleteIfExistsAsync();
            stopWatch.Stop();
            _Log.LogInformation("The Azure Blob " + sourceBlob + " deleted in: " + stopWatch.Elapsed.TotalSeconds + " seconds.");
        }

        public async Task CopyBlobsFromContainerToContainer(CloudBlobContainer sourceContainer, CloudBlobContainer destinationContainer)
        {
            foreach (IListBlobItem item in sourceContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob sourceBlob)
                {
                    CloudBlockBlob destinationBlob = destinationContainer.GetBlockBlobReference(sourceBlob.Name);
                    await destinationBlob.StartCopyAsync(sourceBlob);
                }
            }
        }

        public async Task MoveBlobsFromContainerToContainer(CloudBlobContainer sourceContainer, CloudBlobContainer destinationContainer)
        {
            foreach (IListBlobItem item in sourceContainer.ListBlobs(null, false))
            {
                if (item is CloudBlockBlob sourceBlob)
                {
                    CloudBlockBlob destinationBlob = destinationContainer.GetBlockBlobReference(sourceBlob.Name);
                    await destinationBlob.StartCopyAsync(sourceBlob);
                    await sourceBlob.DeleteIfExistsAsync();
                }
            }
        }

        //Copies a blob between two azure containers.
        public async Task CopyAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob)
        {
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            try
            {
                task = TransferManager.CopyAsync(sourceBlob, destinationBlob, CopyMethod.ServiceSideSyncCopy, null, context, cancellationSource.Token);
                await task;
            }
            catch (AggregateException e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }
            catch (TransferException e)
            {
                _Log.LogInformation($"\nThe Azure Blob {sourceBlob.Name} already exists in {destinationBlob.Parent.Container.Name} with message {e.Message}");
            }
            catch (Exception e)
            {
                e.Data.Add("sourceBlobName", sourceBlob);
                e.Data.Add("destinationBlocName", destinationBlob);
                throw;
            }

            stopWatch.Stop();
            _Log.LogInformation($"The Azure Blob {sourceBlob.Name} transfer to {destinationBlob.Name} completed in: {stopWatch.Elapsed.TotalSeconds} seconds.");
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
                log.LogInformation($"\nNo blob {blobName} found in {containerName} ", e.Message);
                return null;
            }
        }

        //returns an Azure file transfer context for making a single file transfer.
        public SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint)
        {
            try
            {
                SingleTransferContext context = new SingleTransferContext(checkpoint)
                {
                    ProgressHandler = new Progress<TransferStatus>((progress) =>
                    {
                        _Log.LogInformation("\rBytes transferred: {0}", progress.BytesTransferred);
                    })
                };

                return context;
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
            string sasContainerToken;

            SharedAccessBlobPolicy sharedPolicy = new SharedAccessBlobPolicy()
            {
                //******* To Do: change to a more appropriate time than always 1 hour.  Maybe make this configurable.
                SharedAccessStartTime = DateTime.UtcNow.AddHours(1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read
            };

            sasContainerToken = cloudBlockBlob.GetSharedAccessSignature(sharedPolicy);
            return sasContainerToken;
        }

        public string DecodeBase64String(string encodedString)
        {
            var encodedStringWithoutTrailingCharacter = encodedString.Substring(0, encodedString.Length - 1);
            var encodedBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encodedStringWithoutTrailingCharacter);
            return HttpUtility.UrlDecode(encodedBytes, Encoding.UTF8);
        }

        public string EnsureMd5(DataBlob dataBlob)
        {
            string blobMd5 = dataBlob.AzureBlob.Properties.ContentMD5;
            if (blobMd5 == null)
            {
                blobMd5 = dataBlob.CalculateMD5Hash().ToString();
                if (blobMd5 == null)
                {
                    throw (new MissingRequiredObject("\nMD5 calculation failed for " + dataBlob.AzureBlob.Name));
                }
                else
                {
                    dataBlob.AzureBlob.Properties.ContentMD5 = blobMd5;
                }
            }
            return blobMd5;
        }

        public string EncodeMd5HashForFileName(string md5Hash)
        {
            string md5HashNoPadding = md5Hash;
            do
            {
                md5HashNoPadding = (md5HashNoPadding[md5HashNoPadding.Length - 1].ToString() == "=") ? md5HashNoPadding.Remove(md5HashNoPadding.Length - 1) : md5HashNoPadding;
            } while (md5HashNoPadding[md5HashNoPadding.Length - 1].ToString() == "=");

            // If the hash contains a % value then it has already been encoded and encoding it a second time will give a value that does not match the file name
            if (!md5HashNoPadding.Contains("%"))
            {
                return Uri.EscapeDataString(md5HashNoPadding);
            }

            return md5HashNoPadding;
        }

        public string GetEncodedHashFileName(string md5Hash)
        {
            string encodedHash = EncodeMd5HashForFileName(md5Hash);
            return encodedHash + ".json";
        }

        public async Task UploadJsonBlob(CloudBlockBlob jsonBlob, JObject jsonBlobJObject)
        {
            //upload updated Json blob to cloud container
            string serializedJsonBlob = JsonConvert.SerializeObject(jsonBlobJObject, Formatting.Indented, new JsonSerializerSettings { });
            Stream jsonBlobMemStream = new MemoryStream(Encoding.UTF8.GetBytes(serializedJsonBlob));
            if (jsonBlobMemStream.Length != 0)
            {
                await jsonBlob.UploadFromStreamAsync(jsonBlobMemStream);
            }
            else
            {
                throw (new ZeroLengthFileException("\nEncoded JSON memory stream is zero length and cannot be writted to blob storage"));
            }
        }
        public async Task<string> GetKeyVaultSecret(string secretName)
        {
            try
            {
                /* The next four lines of code show you how to use AppAuthentication library to fetch secrets from your key vault */
                AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
                KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
                string keyVaultName = GetEnvironmentVariable("KeyVaultName", _Log);
                var secret = await keyVaultClient.GetSecretAsync($"https://{keyVaultName}.vault.azure.net/secrets/{secretName}")
                        .ConfigureAwait(false);
                return secret.Value;
            }
            /* If you have throttling errors see this tutorial https://docs.microsoft.com/azure/key-vault/tutorial-net-create-vault-azure-web-app */
            /// <exception cref="KeyVaultErrorException">
            /// Thrown when the operation returned an invalid status code
            /// </exception>
            //catch (KeyVaultErrorException keyVaultException)
            catch
            {
                throw;
            }
        }

        public string GetBlobSasTokenForServiceAccess(CloudBlockBlob blob)
        {
            // Set blob SAS access contraints
            // allows access for 1 hour because this is only for service access it does not need to last that long *****TODO***** need to externalize the start and end time so that it can be tuned depending on the performance of the model calls
            SharedAccessBlobPolicy sasContraints = new SharedAccessBlobPolicy();
            sasContraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            sasContraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(1);
            sasContraints.Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write;

            // Generate blob sas token
            string blobSasToken = blob.GetSharedAccessSignature(sasContraints);

            return blob.Uri + blobSasToken;
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
