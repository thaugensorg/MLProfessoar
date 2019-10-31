using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class is the event handler for the Azure blob trigger to evaluate a data blob when a data blob is 
    // uploaded to the pendingevaluation blob container.
    //**********************************************************************************************************

    public static class DataEvaluator
    {
        [FunctionName("EvaluateData")]

        // public static async Task RunAsync([BlobTrigger("pendingevaluation/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        public static async Task RunAsync([BlobTrigger("pendingevaluation/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)


        {
            Engine engine = new Engine(log);

            log.LogInformation($"\nInitiating evaluation of: {blobName}");

            Search search = new Search(engine, log);
            Model model = new Model(engine, search, log);
            try
            {
                string result = await model.EvaluateData(blobName);
                log.LogInformation($"\nAzure Function, completed evaluation of data blob: {blobName}");
            }
            catch (MissingRequiredObject e)
            {
                log.LogInformation($"\nAzure Function, EvaluateData failed to find data blob: {blobName} with error: {e.Message}");
            }
            catch (Exception e)
            {
                log.LogInformation($"\nAzure Function, EvaluateData failed to evaluate data blob: {blobName} with error: {e.Message}");
            }
        }
    }
}
