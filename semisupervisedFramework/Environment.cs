using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;


namespace semisupervisedFramework
{
    class Environment
    {
        //Returns an environemtn variable matching the name parameter in the current app context
        public static string GetEnvironmentVariable(string name, ILogger log)
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

        public static CloudStorageAccount GetStorageAccount(ILogger log)
        {
            string StorageConnection = GetEnvironmentVariable("AzureWebJobsStorage", log);
            CloudStorageAccount StorageAccount = CloudStorageAccount.Parse(StorageConnection);
            return StorageAccount;
        }
    }
}
