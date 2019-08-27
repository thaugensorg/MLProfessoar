using System;
using System.Configuration;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
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
using System.Web;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Management.CognitiveServices.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace semisupervisedFramework
{
    public static class DataEvaluator
    {
        [FunctionName("EvaluateData")]

        public static async Task RunAsync([BlobTrigger("pendingevaluation/{blobName}", Connection = "AzureWebJobsStorage")]Stream myBlob, string blobName, ILogger log)
        {
            Model model = new Model(log);
            try
            {
                string result = model.EvaluateData(blobName);
            }
            catch
            {
                log.LogInformation($"\nAzure Function, EvaluateData failed to evaluate data blob: {blobName}");
            }
        }

        //Builds a URL to call the blob analysis model.
        private static string ConstructModelRequestUrl(string dataEvaluatingUrl, ILogger log)
        {
            try
            {
                //get environment variables used to construct the model request URL
                string ModelServiceEndpoint = GetEnvironmentVariable("EvaluationServiceEndpoint", log);

                if (ModelServiceEndpoint == null || ModelServiceEndpoint == "") 
                {
                    throw (new EnvironmentVariableNotSetException("EvaluationServiceEndpoint environment variable not set"));
                }
                string ModelAssetParameterName = GetEnvironmentVariable("modelAssetParameterName", log);

                //construct model request URL
                string ModelRequestUrl = ModelServiceEndpoint;
                if (ModelAssetParameterName != null & ModelAssetParameterName != "")
                {
                    ModelRequestUrl = ModelRequestUrl + "?" + ModelAssetParameterName + "=";
                    ModelRequestUrl = ModelRequestUrl + dataEvaluatingUrl;
                }
                else
                {
                    throw (new EnvironmentVariableNotSetException("modelAssetParameterName environment variable not set"));
                }

                return ModelRequestUrl;
            }
            catch (EnvironmentVariableNotSetException e)
            {
                log.LogInformation(e.Message);
                return null;
            }
        }

        //Returns an environment variable matching the name parameter in the current app context
        //Need to replace this with the Environment class calls
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

    }

    public class EnvironmentVariableNotSetException : Exception
    {
        public EnvironmentVariableNotSetException(string message)
            : base(message)
        {
        }
    }

    public class InvalidUrlException : Exception
    {
        public InvalidUrlException(string message)
            : base(message)
        {
        }
    }

    public class ZeroLengthFileException : Exception
    {
        public ZeroLengthFileException(string message)
            : base(message)
        {
        }
    }

    public class MissingRequiredObject : Exception
    {
        public MissingRequiredObject(string message)
            : base(message)
        {
        }
    }
}
