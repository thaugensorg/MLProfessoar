using System.IO;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;


namespace semisupervisedFramework
{
    public static class LabelDataTrigger
    {
        [FunctionName("LabelDataTrigger")]
        public static void Run([BlobTrigger("labelingoutput/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            // Do not process the vott project file.
            //*****TODO***** need to externalize this so that you can configure exactly what files are processed and which are not.
            if (!blobName.ToLower().Contains("vott"))
            {
                Engine engine = new Engine(log);
                Search search = new Search(engine);
                Model model = new Model(engine, search);

                // Add new labeling solutions here for every new labeling tool that is integrated with ML Professoar
                DataLabelerFactory dataLabelerFactory = null;
                string labelingSolutionName = engine.GetEnvironmentVariable("labelingSolutionName");
                switch (labelingSolutionName)
                {
                    case "VoTT":
                        dataLabelerFactory = new VottDataLabelerFactory(engine, search, model);
                        DataLabeler dataLabeler = dataLabelerFactory.GetDataLabeler();
                        dataLabeler.LabelData(blobName);
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
