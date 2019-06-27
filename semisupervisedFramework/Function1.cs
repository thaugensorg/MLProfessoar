using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace semisupervisedFramework
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static void Run([BlobTrigger("pendingevaluation/{name}", Connection = "DefaultEndpointsProtocol=https;AccountName=semisupervisedstorage;AccountKey=XBHB5fxDqFAZQLRzcN/C/QLiR+55obGzE7hDdRjSsD07mcNwmpwFnH2MZWayPajGSiXRl4wO3rUFbKiXnSpPaw==;EndpointSuffix=core.windows.net")]Stream myBlob, string name, ILogger log)
        {
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
        }
    }
}
