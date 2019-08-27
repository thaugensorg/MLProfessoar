using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Net.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using semisupervisedFramework.Exceptions;

namespace semisupervisedFramework
{
    class Engine
    {

        //Returns an environemtn variable matching the name parameter in the current app context
        public static string GetEnvironmentVariable(string name, ILogger log = null)
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

        public CloudStorageAccount GetStorageAccount()
        {
            string StorageConnection = GetEnvironmentVariable("AzureWebJobsStorage", log);
            CloudStorageAccount StorageAccount = CloudStorageAccount.Parse(StorageConnection);
            return StorageAccount;
        }

        public static JProperty GetEnvironmentJson(ILogger log)
        {
            //create environment JSON object
            //Dont include storage connection as it contains the storage key which should not be placed in storage.
            JProperty BlobEnvironment =
                new JProperty("environment",
                    new JObject(
                        new JProperty("endpoint", GetEnvironmentVariable("modelServiceEndpoint", log)),
                        new JProperty("parameter", GetEnvironmentVariable("modelAssetParameterName", log)),
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
            return BlobEnvironment;
        }
    }
}
