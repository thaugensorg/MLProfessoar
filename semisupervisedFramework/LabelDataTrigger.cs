using System;
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
                //*****TODO***** eliminate the factory model and replace with C# activate call.
                string labelingSolutionName = engine.GetEnvironmentVariable("labelingSolutionName");
                labelingSolutionName = $"semisupervisedFramework.{labelingSolutionName}DataLabeler";
                Type classType = Type.GetType(labelingSolutionName, true, true);
                if (classType != null)
                {
                    DataLabeler dataLabeler = (DataLabeler)Activator.CreateInstance(classType, new Object[] { engine, search, model });
                    dataLabeler.LabelData(blobName);
                }
                else
                {
                    log.LogInformation($"LabelDataTrigger blob trigger function failed processing blob Name:{blobName}");
                }
            }
            log.LogInformation($"LabelDataTrigger blob trigger function Processed blob Name:{blobName}");
        }
    }
}
