using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.IO;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class marshals the data science code of the model.  All calls to the data science model are handled 
    // via simple http with a very limited set of required parameters.  This allows a data scientist to provide
    // model code in virtually any language on any platform in any location.  Note: ML Professor handles all
    // orchestration of the model using either Azure blob triggers or Azure timer triggers.  That is the definition
    // of orchestration as a result a data scientist only needs to provide behavior for labeling data (generally
    // using open source tools), loading labeled training data, training, and evaluating data.  All other aspects
    // of taking action and moving files is handled by the orchestration engine.
    //**********************************************************************************************************

    class Model
    {
        private ILogger _Log;
        private string _TrainModelUrl;
        private HttpClient _Client;
        private HttpResponseMessage _Response;
        private string _ResponseString = "";
        private Engine _Engine;
        private Search _Search;

        public Model(Engine engine, Search search, ILogger log)
        {
            _Log = log;
            _Client = new HttpClient();
            _Engine = engine;
            _Search = search;
            _TrainModelUrl = _Engine.GetEnvironmentVariable("TrainModelServiceEndpoint", _Log);
            if (string.IsNullOrEmpty(_TrainModelUrl)) throw (new EnvironmentVariableNotSetException("TrainModelServiceEndpoint environment variable not set"));
        }

        public string AddLabeledData()
        {
            string TrainingDataUrl;
            CloudStorageAccount StorageAccount = _Engine.StorageAccount;
            CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
            //*****TODO***** externalize labeled data container name.
            CloudBlobContainer LabeledDataContainer = BlobClient.GetContainerReference("labeleddata");

            foreach (IListBlobItem item in LabeledDataContainer.ListBlobs(null, false))
            {
                if (item.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob dataCloudBlockBlob = (CloudBlockBlob)item;
                    TrainingDataUrl = dataCloudBlockBlob.Uri.ToString();
                    string BindingHash = dataCloudBlockBlob.Properties.ContentMD5.ToString();
                    if (BindingHash == null)
                    {
                        //compute the file hash as this will be added to the meta data to allow for file version validation
                        string BlobMd5 = new DataBlob(dataCloudBlockBlob, _Search, _Log).CalculateMD5Hash().ToString();
                        if (BlobMd5 == null)
                        {
                            _Log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                        }
                        else
                        {
                            dataCloudBlockBlob.Properties.ContentMD5 = BlobMd5;
                        }

                    }
                    //trim the 2 "equals" off the trailing end of the hash or the http send will fail either using the client or raw http calls.
                    BindingHash = BindingHash.Substring(0, BindingHash.Length - 2);

                    //Get the content from the bound JSON file and instanciate a JsonBlob class then retrieve the labels collection from the Json to add to the image.
                    JsonBlob boundJson = (JsonBlob)_Search.GetBlob("json", BindingHash);
                    string AddLabeledDataUrl = boundJson.BlobInfo.Url;
                    string addLabeledDataParameters = $"?blobUrl={AddLabeledDataUrl}";
                    string trainingDataLabels = Uri.EscapeDataString(JsonConvert.SerializeObject(boundJson.Labels));
                    addLabeledDataParameters = $"{addLabeledDataParameters}&imageLabels={trainingDataLabels}";

                    //construct and call model URL then fetch response
                    // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use
                    //{ {environment variable name}}.
                    // So the example load labels function in the sameple model package would look like this:
                    // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                    // The orchestration engine appends the labels json file to the message body.
                    // http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                    AddLabeledDataUrl = _Engine.ConstructModelRequestUrl(AddLabeledDataUrl, addLabeledDataParameters);
                    _Response = _Client.GetAsync(AddLabeledDataUrl).Result;
                    _ResponseString = _Response.Content.ReadAsStringAsync().Result;
                    if (string.IsNullOrEmpty(_ResponseString)) throw (new MissingRequiredObject($"\nresponseString not generated from URL: {AddLabeledDataUrl}"));

                    //the code below is for passing labels and conent as http content and not on the URL string.
                    //Format the Data Labels content
                    //HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Post, new Uri(AddLabeledDataUrl));
                    //HttpContent DataLabelsStringContent = new StringContent(trainingDataLabels, Encoding.UTF8, "application/x-www-form-urlencoded");
                    //MultipartFormDataContent LabeledDataContent = new MultipartFormDataContent();
                    //LabeledDataContent.Add(DataLabelsStringContent, "LabeledData");

                    //Format the data cotent
                    //*****TODO***** move to an async architecture
                    //*****TODO***** need to decide if there is value in sending the data as a binary stream in the post or if requireing the model data scienctist to accept URLs is sufficient.  If accessing the data blob with a SAS url requires Azure classes then create a configuration to pass the data as a stream in the post.  If there is then this should be a configurable option.
                    //MemoryStream dataBlobMemStream = new MemoryStream();
                    //dataBlob.DownloadToStream(dataBlobMemStream);
                    //HttpContent LabeledDataHttpContent = new StreamContent(dataBlobMemStream);
                    //LabeledDataContent.Add(LabeledDataContent, "LabeledData");

                    //Make the http call and get a response
                    //string AddLabelingTagsEndpoint = Engine.GetEnvironmentVariable("LabeledDataServiceEndpoint", log);
                    //if (string.IsNullOrEmpty(AddLabelingTagsEndpoint)) throw (new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set"));
                    //string ResponseString = Helper.GetEvaluationResponseString(AddLabelingTagsEndpoint, LabeledDataContent, log);
                    //if (string.IsNullOrEmpty(ResponseString)) throw (new MissingRequiredObject("\nresponseString not generated from URL: " + AddLabelingTagsEndpoint));

                    _Log.LogInformation($"Successfully added blob: {dataCloudBlockBlob.Name} with labels: {JsonConvert.SerializeObject(boundJson.Labels)}");
                }
            }
            return "Completed execution of AddLabeledData.  See logs for success/fail details.";
        }

        public string LoadTrainingTags()
        {
            string responseString = "";

            //Construct a blob client to marshall the storage Functionality
            CloudStorageAccount storageAccount = _Engine.StorageAccount;
            CloudBlobClient LabelsBlobClient = storageAccount.CreateCloudBlobClient();

            //Construct a blob storage container given a name string and a storage account
            string jsonDataContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
            if (string.IsNullOrEmpty(jsonDataContainerName)) throw (new EnvironmentVariableNotSetException("jsonStorageContainerName environment variable not set"));

            CloudBlobContainer Container = LabelsBlobClient.GetContainerReference(jsonDataContainerName);

            //get the training tags json blob from the container
            string DataTagsBlobName = _Engine.GetEnvironmentVariable("dataTagsBlobName", _Log);
            if (string.IsNullOrEmpty(DataTagsBlobName)) throw (new EnvironmentVariableNotSetException("dataTagsBlobName environment variable not set"));

            CloudBlockBlob DataTagsBlob = Container.GetBlockBlobReference(DataTagsBlobName);

            //the blob has to be "touched" or the properties will all be null
            if (DataTagsBlob.Exists() != true)
            {
                _Log.LogInformation("The labeling tags blob exists");
            };

            //get the environment variable specifying the MD5 hash of the last run tags file
            string LkgDataTagsFileHash = _Engine.GetEnvironmentVariable("dataTagsFileHash", _Log);

            //Check if there is a new version of the tags json file and if so load them into the environment
            if (DataTagsBlob.Properties.ContentMD5 != LkgDataTagsFileHash)
            {
                //format the http call to load labeling tags
                string AddLabelingTagsEndpoint = _Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", _Log);
                if (string.IsNullOrEmpty(AddLabelingTagsEndpoint)) throw (new EnvironmentVariableNotSetException("TagsUploadServiceEndpoint environment variable not set"));
                string LabelingTagsParamatersName = _Engine.GetEnvironmentVariable("tagDataParameterName", _Log);
                string LabelingTags = DataTagsBlob.DownloadText(Encoding.UTF8);
                HttpContent LabelingTagsContent = new StringContent(LabelingTags);
                var content = new MultipartFormDataContent();
                content.Add(LabelingTagsContent, "LabelsJson");

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                //string DataTagsUrl = DataTagsBlob.Uri.ToString(); //+ dataEvaluatingSas;

                //Make a request to the model service load labeling tags function passing the tags.
                responseString = _Engine.GetEvaluationResponseString(AddLabelingTagsEndpoint, content, _Log);
                if (string.IsNullOrEmpty(responseString)) throw (new MissingRequiredObject("\nresponseString not generated from URL: " + AddLabelingTagsEndpoint));

                //save the hash of this version of the labeling tags file so that we can avoid running load labeling tags if the file has not changed.
                System.Environment.SetEnvironmentVariable("dataTagsFileHash", DataTagsBlob.Properties.ContentMD5);
                _Log.LogInformation(responseString);
            }
            return $"Training tags process executed with response: {responseString}";
        }

        public string Train()
        {
            //Invoke the train model web service call
            _Response = _Client.GetAsync(_TrainModelUrl).Result;
            _ResponseString = _Response.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrEmpty(_ResponseString)) throw (new MissingRequiredObject($"\nresponseString not generated from URL: {_TrainModelUrl}"));
            return _ResponseString;

        }

        public string EvaluateData(string blobName)
        {
            try
            {
                string PendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
                string EvaluatedDataStorageContainerName = _Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName", _Log);
                string JsonStorageContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
                string PendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
                string LabeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
                string ModelValidationStorageContainerName = _Engine.GetEnvironmentVariable("modelValidationStorageContainerName", _Log);
                string PendingNewModelStorageContainerName = _Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName", _Log);
                string StorageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
                string ConfidenceJsonPath = _Engine.GetEnvironmentVariable("confidenceJSONPath", _Log);
                string DataTagsBlobName = _Engine.GetEnvironmentVariable("dataTagsBlobName", _Log);
                double ConfidenceThreshold = Convert.ToDouble(_Engine.GetEnvironmentVariable("confidenceThreshold", _Log));
                double ModelVerificationPercent = Convert.ToDouble(_Engine.GetEnvironmentVariable("modelVerificationPercentage", _Log));

                //------------------------This section retrieves the blob needing evaluation and calls the evaluation service for processing.-----------------------

                // Create Reference to Azure Storage Account and the container for data that is pending evaluation by the model.
                CloudStorageAccount StorageAccount = CloudStorageAccount.Parse(StorageConnection);
                CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
                CloudBlobContainer Container = BlobClient.GetContainerReference(PendingEvaluationStorageContainerName);
                CloudBlockBlob rawDataBlob = Container.GetBlockBlobReference(blobName);
                DataBlob dataEvaluating = new DataBlob(rawDataBlob, _Search, _Log);
                //CloudBlockBlob RawDataBlob = _Search.GetBlob(StorageAccount, JsonStorageContainerName, blobName, _Log);
                //DataBlob DataEvaluating = new DataBlob(RawDataBlob.Properties.ContentMD5, _Engine, _Search, _Log);
                if (dataEvaluating == null)
                {
                    throw (new MissingRequiredObject("\nMissing dataEvaluating blob object."));
                }

                //compute the file hash as this will be added to the meta data to allow for file version validation
                string blobMd5 = dataEvaluating.CalculateMD5Hash().ToString();
                if (blobMd5 == null)
                {
                    _Log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                }
                else
                {
                    dataEvaluating.AzureBlob.Properties.ContentMD5 = blobMd5;
                }

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                string DataEvaluatingUrl = dataEvaluating.AzureBlob.Uri.ToString(); //+ dataEvaluatingSas;
                //string dataEvaluatingUrl = "test";

                //package the file contents to send as http request content
                //MemoryStream DataEvaluatingContent = new MemoryStream();
                //DataEvaluating.AzureBlob.DownloadToStreamAsync(DataEvaluatingContent);
                //HttpContent DataEvaluatingStream = new StreamContent(DataEvaluatingContent);
                var content = new MultipartFormDataContent();
                //content.Add(DataEvaluatingStream, "name");

                //get environment variables used to construct the model request URL
                string labeledDataServiceEndpoint = _Engine.GetEnvironmentVariable("DataEvaluationServiceEndpoint", _Log);

                if (labeledDataServiceEndpoint == null || labeledDataServiceEndpoint == "")
                {
                    throw (new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set"));
                }

                string evaluationDataParameterName = _Engine.GetEnvironmentVariable("modelAssetParameterName", _Log);
                if (evaluationDataParameterName == null || evaluationDataParameterName == "")
                {
                    throw (new EnvironmentVariableNotSetException("LabeledDataServiceEndpoint environment variable not set"));
                }

                string parameters = $"?{evaluationDataParameterName}={DataEvaluatingUrl}";
                string evaluateData = _Engine.ConstructModelRequestUrl(labeledDataServiceEndpoint, parameters);

                //Make a request to the model service passing the file URL
                string ResponseString = _Engine.GetEvaluationResponseString(DataEvaluatingUrl, content, _Log);
                if (ResponseString == "")
                {
                    throw (new MissingRequiredObject("\nresponseString not generated from URL: " + DataEvaluatingUrl));
                }

                //deserialize response JSON, get confidence score and compare with confidence threshold
                JObject AnalysisJson = JObject.Parse(ResponseString);
                string StrConfidence = (string)AnalysisJson.SelectToken(ConfidenceJsonPath);
                double Confidence = (double)AnalysisJson.SelectToken(ConfidenceJsonPath);
                if (StrConfidence == null)
                {
                    throw (new MissingRequiredObject("\nNo confidence value at " + ConfidenceJsonPath + " from environment variable ConfidenceJSONPath."));
                }

                //--------------------------------This section processes the results of the analysis and transferes the blob to the container responsible for the next appropriate stage of processing.-------------------------------

                //model successfully analyzed content
                if (Confidence >= ConfidenceThreshold)
                {
                    CloudBlockBlob EvaluatedData = _Search.GetBlob(StorageAccount, EvaluatedDataStorageContainerName, blobName, _Log);
                    if (EvaluatedData == null)
                    {
                        throw (new MissingRequiredObject("\nMissing evaluatedData " + blobName + " destination blob in container " + EvaluatedDataStorageContainerName));
                    }
                    _Engine.CopyAzureBlobToAzureBlob(StorageAccount, dataEvaluating.AzureBlob, EvaluatedData, _Log).Wait();

                    //pick a random number of successfully analyzed content blobs and submit them for supervision verification.
                    Random Rnd = new Random();
                    if (Math.Round(Rnd.NextDouble(), 2) <= ModelVerificationPercent)
                    {
                        CloudBlockBlob ModelValidation = _Search.GetBlob(StorageAccount, ModelValidationStorageContainerName, blobName, _Log);
                        if (ModelValidation == null)
                        {
                            _Log.LogInformation("\nWarning: Model validation skipped for " + blobName + " because of missing evaluatedData " + blobName + " destination blob in container " + ModelValidationStorageContainerName);
                        }
                        else
                        {
                            _Engine.MoveAzureBlobToAzureBlob(StorageAccount, dataEvaluating.AzureBlob, ModelValidation, _Log).Wait();
                        }
                    }
                    dataEvaluating.AzureBlob.DeleteIfExistsAsync();
                }

                //model was not sufficiently confident in its analysis
                else
                {
                    CloudBlockBlob PendingSupervision = _Search.GetBlob(StorageAccount, PendingSupervisionStorageContainerName, blobName, _Log);
                    if (PendingSupervision == null)
                    {
                        throw (new MissingRequiredObject("\nMissing pendingSupervision " + blobName + " destination blob in container " + PendingSupervisionStorageContainerName));
                    }

                    _Engine.MoveAzureBlobToAzureBlob(StorageAccount, dataEvaluating.AzureBlob, PendingSupervision, _Log).Wait();
                }

                //----------------------------This section collects information about the blob being analyzied and packages it in JSON that is then written to blob storage for later processing-----------------------------------

                JObject BlobAnalysis =
                    new JObject(
                        new JProperty("id", Guid.NewGuid().ToString()),
                        new JProperty("blobInfo",
                            new JObject(
                                new JProperty("name", blobName),
                                new JProperty("url", dataEvaluating.AzureBlob.Uri.ToString()),
                                new JProperty("modified", dataEvaluating.AzureBlob.Properties.LastModified.ToString()),
                                new JProperty("hash", blobMd5)
                            )
                        )
                    );

                //create environment JSON object
                JProperty BlobEnvironment = _Engine.GetEnvironmentJson(_Log);

                BlobAnalysis.Add(BlobEnvironment);
                BlobAnalysis.Merge(AnalysisJson);

                //Note: all json files get writted to the same container as they are all accessed either by discrete name or by azure search index either GUID or Hash.
                CloudBlockBlob JsonBlob = _Search.GetBlob(StorageAccount, JsonStorageContainerName, (string)BlobAnalysis.SelectToken("blobInfo.id") + ".json", _Log);
                JsonBlob.Properties.ContentType = "application/json";
                string SerializedJson = JsonConvert.SerializeObject(BlobAnalysis, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { });
                Stream MemStream = new MemoryStream(Encoding.UTF8.GetBytes(SerializedJson));
                if (MemStream.Length != 0)
                {
                    JsonBlob.UploadFromStreamAsync(MemStream);
                }
                else
                {
                    throw (new ZeroLengthFileException("\nencoded JSON memory stream is zero length and cannot be writted to blob storage"));
                }
                _Log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName}");
            }
            catch (MissingRequiredObject e)
            {
                _Log.LogInformation("\n" + blobName + " could not be analyzed with message: " + e.Message);
            }
            catch (Exception e)
            {
                _Log.LogInformation("\n" + blobName + " could not be analyzed with message: " + e.Message);
            }
            return $"Evaluate data completed evaluating data blob: {blobName}";
        }
    }
}
