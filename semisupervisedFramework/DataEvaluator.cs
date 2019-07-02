using System;
using System.Configuration;
using System.IO;
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
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.DataMovement;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.Azure.Management.CognitiveServices.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace semisupervisedFramework
{
    public static class DataEvaluator
    {
        [FunctionName("EvaluateData")]

        public static void Run([BlobTrigger("pendingevaluation/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            string pendingEvaluationStorageContainerName = "pendingevaluation";
            string evaluatedDataStorageContainerName = "evaluateddata";
            string storageConnection = GetEnvironmentVariable("AzureWebJobsStorage");
            string subscriptionKey = GetEnvironmentVariable("CognitiveServicesKey");

            // Specify the features to return
            List<VisualFeatureTypes> features =
            new List<VisualFeatureTypes>()
            {
            VisualFeatureTypes.Brands, VisualFeatureTypes.Description,
            VisualFeatureTypes.ImageType, VisualFeatureTypes.Tags
            };

            // Create Reference to Azure Storage Account
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
            
            //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
            CloudBlockBlob dataEvaluating = GetBlob(storageAccount, pendingEvaluationStorageContainerName, blobName);

            //Instanciate a computer vision client to submit image for analysis
            //ComputerVisionClient computerVision = new ComputerVisionClient(
            //new ApiKeyServiceClientCredentials(subscriptionKey),
            //new System.Net.Http.DelegatingHandler[] { });

            // Specify the Azure region
            //computerVision.Endpoint = "https://westus.api.cognitive.microsoft.com";

            //Currently only working with public access set on blob folders
            //Generate a URL with SAS token to submit to analyze image API
            //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
            //string dataEvaluatingUrl = dataEvaluating.Uri.ToString(); //+ dataEvaluatingSas;

            //Code works with this publically available image address
            //var t1 = AnalyzeRemoteAsync(computerVision, dataEvaluatingUrl, features);

            //Make a request to the model service passing the file URL
            HttpClient client = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://branddetectionapp.azurewebsites.net/api/detectBrand"));
            HttpResponseMessage response = client.SendAsync(request).Result;
            var responseString = response.Content.ReadAsStringAsync().Result;

            //CloudBlockBlob evaluatedData = GetBlob(storageAccount, evaluatedDataStorageContainerName, blobName);

            //TransferAzureBlobToAzureBlob(storageAccount, dataEvaluating, evaluatedData).Wait();

            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
        }

        public static string GetEnvironmentVariable(string name)
        {
            return System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }

        public static async Task TransferAzureBlobToAzureBlob(CloudStorageAccount account, CloudBlockBlob sourceBlob, CloudBlockBlob destinationBlob)
        {
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Console.WriteLine("\nTransfer started...\nPress 'c' to temporarily cancel your transfer...\n");

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            ConsoleKeyInfo keyinfo;
            try
            {
                task = TransferManager.CopyAsync(sourceBlob, destinationBlob, true, null, context, cancellationSource.Token);
                while (!task.IsCompleted)
                {
                    if (Console.KeyAvailable)
                    {
                        keyinfo = Console.ReadKey(true);
                        if (keyinfo.Key == ConsoleKey.C)
                        {
                            cancellationSource.Cancel();
                        }
                    }
                }
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine("\nThe transfer is canceled: {0}", e.Message);
            }

            if (cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("\nTransfer will resume in 3 seconds...");
                Thread.Sleep(3000);
                checkpoint = context.LastCheckpoint;
                context = GetSingleTransferContext(checkpoint);
                Console.WriteLine("\nResuming transfer...\n");
                await TransferManager.CopyAsync(sourceBlob, destinationBlob, false, null, context, cancellationSource.Token);
            }

            stopWatch.Stop();
            Console.WriteLine("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
            //ExecuteChoice(account);
        }
        //Gets a reference to a specific blob
        public static CloudBlockBlob GetBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            return blob;
        }

        public static CloudBlobContainer GetBlobContainer(CloudStorageAccount account, string containerName)
        {
            string containerPolicyName = "evaluationPolicy";

            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(containerName);
            blobContainer.CreateIfNotExistsAsync().Wait();

            // create the stored policy we will use, with the relevant permissions and expiry time
            SharedAccessBlobPolicy storedPolicy = new SharedAccessBlobPolicy()
            {
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(10),
                Permissions = SharedAccessBlobPermissions.Read |
                              SharedAccessBlobPermissions.Write |
                              SharedAccessBlobPermissions.List
            };

            // get the existing permissions (alternatively create new BlobContainerPermissions())
            //BlobContainerPermissions permissions = await blobContainer.GetPermissionsAsync();

            // optionally clear out any existing policies on this container
            //permissions.SharedAccessPolicies.Clear();
            // add in the new one
            //permissions.SharedAccessPolicies.Add(containerPolicyName, storedPolicy);
            // save back to the container
            //blobContainer.SetPermissionsAsync(permissions);


            return blobContainer;
        }
        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint)
        {
            SingleTransferContext context = new SingleTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                Console.Write("\rBytes transferred: {0}", progress.BytesTransferred);
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
        private static async Task AnalyzeRemoteAsync(ComputerVisionClient computerVision, string imageUrl, List<VisualFeatureTypes> features)
        {
            if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                Console.WriteLine(
                    "\nInvalid remoteImageUrl:\n{0} \n", imageUrl);
                return;
            }

            //Analyze image and display the results in the console
            ImageAnalysis analysis = await computerVision.AnalyzeImageAsync(imageUrl, features);
            DisplayResults(analysis, imageUrl);
        }
        // Display the most relevant caption for the image
        private static void DisplayResults(ImageAnalysis analysis, string imageUri)
        {
            Console.WriteLine(imageUri);
            if (analysis.Description.Captions.Count != 0)
            {
                Console.WriteLine(analysis.Description.Captions[0].Text + "\n");
            }
            else
            {
                Console.WriteLine("No description generated.");
            }

        }
    }

        public class Program
    {
        public static SingleTransferContext GetSingleTransferContext(TransferCheckpoint checkpoint)
        {
            SingleTransferContext context = new SingleTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                Console.Write("\rBytes transferred: {0}", progress.BytesTransferred);
            });

            return context;
        }

        public static DirectoryTransferContext GetDirectoryTransferContext(TransferCheckpoint checkpoint)
        {
            DirectoryTransferContext context = new DirectoryTransferContext(checkpoint);

            context.ProgressHandler = new Progress<TransferStatus>((progress) =>
            {
                Console.Write("\rBytes transferred: {0}", progress.BytesTransferred);
            });

            return context;
        }

        public static void SetNumberOfParallelOperations()
        {
            Console.WriteLine("\nHow many parallel operations would you like to use?");
            string parallelOperations = Console.ReadLine();
            TransferManager.Configurations.ParallelOperations = int.Parse(parallelOperations);
        }

        public static string GetSourcePath()
        {
            Console.WriteLine("\nProvide path for source:");
            string sourcePath = Console.ReadLine();

            return sourcePath;
        }

        public static CloudBlockBlob GetBlob(CloudStorageAccount account)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();

            Console.WriteLine("\nProvide name of Blob container:");
            string containerName = Console.ReadLine();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            Console.WriteLine("\nProvide name of Blob:");
            string blobName = Console.ReadLine();
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            return blob;
        }

        public static CloudBlobDirectory GetBlobDirectory(CloudStorageAccount account)
        {
            CloudBlobClient blobClient = account.CreateCloudBlobClient();

            Console.WriteLine("\nProvide name of Blob container:");
            string containerName = Console.ReadLine();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExistsAsync().Wait();

            CloudBlobDirectory blobDirectory = container.GetDirectoryReference("");

            return blobDirectory;
        }

        public static async Task TransferLocalFileToAzureBlob(CloudStorageAccount account)
        {
            string localFilePath = GetSourcePath();
            CloudBlockBlob blob = GetBlob(account);
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Console.WriteLine("\nTransfer started...\nPress 'c' to temporarily cancel your transfer...\n");

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            ConsoleKeyInfo keyinfo;
            try
            {
                task = TransferManager.UploadAsync(localFilePath, blob, null, context, cancellationSource.Token);
                while (!task.IsCompleted)
                {
                    if (Console.KeyAvailable)
                    {
                        keyinfo = Console.ReadKey(true);
                        if (keyinfo.Key == ConsoleKey.C)
                        {
                            cancellationSource.Cancel();
                        }
                    }
                }
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine("\nThe transfer is canceled: {0}", e.Message);
            }

            if (cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("\nTransfer will resume in 3 seconds...");
                Thread.Sleep(3000);
                checkpoint = context.LastCheckpoint;
                context = GetSingleTransferContext(checkpoint);
                Console.WriteLine("\nResuming transfer...\n");
                await TransferManager.UploadAsync(localFilePath, blob, null, context);
            }

            stopWatch.Stop();
            Console.WriteLine("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
            //ExecuteChoice(account);
        }

        public static async Task TransferLocalDirectoryToAzureBlobDirectory(CloudStorageAccount account)
        {
            string localDirectoryPath = GetSourcePath();
            CloudBlobDirectory blobDirectory = GetBlobDirectory(account);
            TransferCheckpoint checkpoint = null;
            DirectoryTransferContext context = GetDirectoryTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Console.WriteLine("\nTransfer started...\nPress 'c' to temporarily cancel your transfer...\n");

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            ConsoleKeyInfo keyinfo;
            UploadDirectoryOptions options = new UploadDirectoryOptions()
            {
                Recursive = true
            };

            try
            {
                task = TransferManager.UploadDirectoryAsync(localDirectoryPath, blobDirectory, options, context, cancellationSource.Token);
                while (!task.IsCompleted)
                {
                    if (Console.KeyAvailable)
                    {
                        keyinfo = Console.ReadKey(true);
                        if (keyinfo.Key == ConsoleKey.C)
                        {
                            cancellationSource.Cancel();
                        }
                    }
                }
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine("\nThe transfer is canceled: {0}", e.Message);
            }

            if (cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("\nTransfer will resume in 3 seconds...");
                Thread.Sleep(3000);
                checkpoint = context.LastCheckpoint;
                context = GetDirectoryTransferContext(checkpoint);
                Console.WriteLine("\nResuming transfer...\n");
                await TransferManager.UploadDirectoryAsync(localDirectoryPath, blobDirectory, options, context);
            }

            stopWatch.Stop();
            Console.WriteLine("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
            //ExecuteChoice(account);
        }

        public static async Task TransferUrlToAzureBlob(CloudStorageAccount account)
        {
            Uri uri = new Uri(GetSourcePath());
            CloudBlockBlob blob = GetBlob(account);
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Console.WriteLine("\nTransfer started...\nPress 'c' to temporarily cancel your transfer...\n");

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            ConsoleKeyInfo keyinfo;
            try
            {
                task = TransferManager.CopyAsync(uri, blob, true, null, context, cancellationSource.Token);
                while (!task.IsCompleted)
                {
                    if (Console.KeyAvailable)
                    {
                        keyinfo = Console.ReadKey(true);
                        if (keyinfo.Key == ConsoleKey.C)
                        {
                            cancellationSource.Cancel();
                        }
                    }
                }
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine("\nThe transfer is canceled: {0}", e.Message);
            }

            if (cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("\nTransfer will resume in 3 seconds...");
                Thread.Sleep(3000);
                checkpoint = context.LastCheckpoint;
                context = GetSingleTransferContext(checkpoint);
                Console.WriteLine("\nResuming transfer...\n");
                await TransferManager.CopyAsync(uri, blob, true, null, context, cancellationSource.Token);
            }

            stopWatch.Stop();
            Console.WriteLine("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
            //ExecuteChoice(account);
        }

        public static async Task TransferAzureBlobToAzureBlob(CloudStorageAccount account)
        {
            CloudBlockBlob sourceBlob = GetBlob(account);
            CloudBlockBlob destinationBlob = GetBlob(account);
            TransferCheckpoint checkpoint = null;
            SingleTransferContext context = GetSingleTransferContext(checkpoint);
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            //Console.WriteLine("\nTransfer started...\nPress 'c' to temporarily cancel your transfer...\n");

            Stopwatch stopWatch = Stopwatch.StartNew();
            Task task;
            ConsoleKeyInfo keyinfo;
            try
            {
                task = TransferManager.CopyAsync(sourceBlob, destinationBlob, true, null, context, cancellationSource.Token);
                while (!task.IsCompleted)
                {
                    if (Console.KeyAvailable)
                    {
                        keyinfo = Console.ReadKey(true);
                        if (keyinfo.Key == ConsoleKey.C)
                        {
                            cancellationSource.Cancel();
                        }
                    }
                }
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine("\nThe transfer is canceled: {0}", e.Message);
            }

            if (cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("\nTransfer will resume in 3 seconds...");
                Thread.Sleep(3000);
                checkpoint = context.LastCheckpoint;
                context = GetSingleTransferContext(checkpoint);
                Console.WriteLine("\nResuming transfer...\n");
                await TransferManager.CopyAsync(sourceBlob, destinationBlob, false, null, context, cancellationSource.Token);
            }

            stopWatch.Stop();
            Console.WriteLine("\nTransfer operation completed in " + stopWatch.Elapsed.TotalSeconds + " seconds.");
            //ExecuteChoice(account);
        }
    }
}
