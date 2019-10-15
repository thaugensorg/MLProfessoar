using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;


namespace semisupervisedFramework
{
    public static class LabelDataTrigger
    {
        [FunctionName("LabelDataTrigger")]
        public static void Run([BlobTrigger("labelingoutput/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            // Do not process the vott project file.
            if (blobName.Split('.')[1] != "vott")
            {
                Engine engine = new Engine(log);
                LabelData labelData = new LabelData();

                // Add new labeling solutions here for every new tool that is integrated with ML Professoar
                string labelingSolutionName = engine.GetEnvironmentVariable("labelingSolutionName", log);
                switch (labelingSolutionName)
                {
                    case "VoTT":
                        labelData.VottLabelData(blobName, log);
                        break;

                    default:
                        log.LogInformation($"\nInvalid labeling solution name {labelingSolutionName}.  No method exists to handle this labeling solution type.");
                        break;
                }
            }
            log.LogInformation($"LabelDataTrigger blob trigger function Processed blob\n Name:{blobName} \n Size: {myBlob.Length} Bytes");
        }
    }
}
