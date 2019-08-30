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
        private Search _Search;

        // *****todo***** why doesn't this run???
        public void Initialize(ExtensionConfigContext context) => _Search.InitializeSearch();
    }

    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class is the timer that launches the model training process using labeled data from the labeleddata
    // Azure blob container.
    //**********************************************************************************************************

    public static class TrainingTimer
    {
        //*****TODO***** Externalize timer frequency.
        [FunctionName("TrainingTimer")]
        public static void Run(
                [TimerTrigger("0 */60 * * * *" //setting this to 1 will cause the trigger to fire every minute for debug purposes.

            //This setting causes the timer job to immediately run when you press F5 rather than having to wait for the timer to fire after n minutes.  Set the line below to true if you want to debug the timer process.
#if DEBUG
            , RunOnStartup = false
#endif            

            )]TimerInfo myTimer, ILogger log)
        {

            Engine engine = new Engine(log);
            try
            {
                string responseString = "";
                CloudStorageAccount storageAccount = engine.StorageAccount;
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                //*****TODO***** externalize labeled data container name.
                CloudBlobContainer labeledDataContainer = blobClient.GetContainerReference("labeleddata");

                //*****TODO***** update this to check if there are any new files not just files
                // Check if there are any files in the labeled data container before loading labeling tags follwed by labeled data followed by training the model.
                if (labeledDataContainer.ListBlobs(null, false) != null)
                {
                    Search search = new Search(engine, log);
                    Model model = new Model(engine, search, log);

                    //*****TODO***** Where should search be initialized?  Azure search does not offer CLI calls to configure all of search so it needs to be initialized befor it can be used as a service.  Look at putting it in engine.  Recognize this is not the same thing as migrating search to a non-static mode and then newing it up.
                    //Search.InitializeSearch();

                    //Load the list of valid training tags to ensure all data labels are valid.
                    string loadTrainingTagsResult = model.LoadTrainingTags();

                    //Add full set set of labeled training data to the model
                    //*****TODO***** add logic to only add incremental labeled data to model
                    string addLabeledDataResult = model.AddLabeledData();

                    //Train model using latest labeled training data.
                    string trainingResultsString = model.Train();

                    //Construct response string for system logging.
                    responseString = $"\nModel training complete with the following result:" +
                        $"\nLoading Training Tags results: {loadTrainingTagsResult}" +
                        $"\nAdding Labeled Data results: {addLabeledDataResult}" +
                        $"\nTraining Results: {trainingResultsString}";
                }
                else
                {
                    throw(new MissingRequiredObject($"\n LabeledDataContainer was empty at {DateTime.Now} no model training action taken"));
                }
                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}{responseString}");
            }
            catch (Exception e)
            {
                log.LogInformation($"\nError processing training timer: {e.Message}");
            }
        }
    }
}
