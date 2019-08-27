﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using semisupervisedFramework.Storage;
using System.IO;

namespace semisupervisedFramework.Storage
{
    class Helper
    {
        public CloudStorageAccount GetStorageAccount()
        {
            string StorageConnection = Engine.GetEnvironmentVariable("AzureWebJobsStorage");
            return CloudStorageAccount.Parse(StorageConnection);
        }

        //Moves a blob between two azure containers.
        public static async Task MoveAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob, ILogger log)
        {
            await CopyAzureBlobToAzureBlob(account, sourceBlob, destinationBlob, log);

            var StopWatch = Stopwatch.StartNew();
            await sourceBlob.DeleteIfExistsAsync();
            StopWatch.Stop();
            log.LogInformation("The Azure Blob " + sourceBlob + " deleted in: " + StopWatch.Elapsed.TotalSeconds + " seconds.");
        }


        //Copies a blob between two azure containers.
        public static async Task CopyAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob, ILogger log)
        {
            TransferCheckpoint Checkpoint = null;
            var Context = GetSingleTransferContext(Checkpoint, log);
            var CancellationSource = new CancellationTokenSource();

            var StopWatch = Stopwatch.StartNew();
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

        //returns an Azure file transfer context for making a single file transfer.
        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint, ILogger log)
        {
            try
            {
                var Context = new SingleTransferContext(checkpoint);

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

            var SharedPolicy = new SharedAccessBlobPolicy()
            {
                //******* To Do: change to a more appropriate time than always 1 hour.  Maybe make this configurable.
                SharedAccessStartTime = DateTime.UtcNow.AddHours(1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read
            };

            SasContainerToken = cloudBlockBlob.GetSharedAccessSignature(SharedPolicy);
            return SasContainerToken;
        }

        //Gets a reference to a specific blob using container and blob names as strings
        public string DownloadBlobAsString(CloudStorageAccount account, string containerName, string blobName)
        {
            try
            {
                var BlobClient = account.CreateCloudBlobClient();
                var Container = BlobClient.GetContainerReference(containerName);
                Container.CreateIfNotExistsAsync().Wait();
                var Blob = Container.GetBlockBlobReference(blobName);

                using (var memoryStream = new MemoryStream())
                {
                    Blob.DownloadToStream(memoryStream);
                    using (var streamReader = new StreamReader(memoryStream))
                    {
                        return streamReader.ReadToEnd();
                    }
                }
            }
            catch (Exception e)
            {
                // log.LogInformation("\nNo blob " + blobName + " found in " + containerName + " ", e.Message);
                return null;
            }
        }
    }
}
