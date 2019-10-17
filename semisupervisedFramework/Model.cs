using System;
using System.Text;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;
using System.Management.Automation;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

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
            string modelType = _Engine.GetEnvironmentVariable("modelType", _Log);
        }

        public async Task<string> AddLabeledData()
        {
            string trainingDataUrl;
            CloudStorageAccount storageAccount = _Engine.StorageAccount;
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            string labeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
            CloudBlobContainer labeledDataContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);

            foreach (IListBlobItem item in labeledDataContainer.ListBlobs(null, false))
            {
                if (item.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob dataCloudBlockBlob = (CloudBlockBlob)item;
                    trainingDataUrl = dataCloudBlockBlob.Uri.ToString();
                    string bindingHash = dataCloudBlockBlob.Properties.ContentMD5.ToString();
                    if (bindingHash == null)
                    {
                        //compute the file hash as this will be added to the meta data to allow for file version validation
                        string BlobMd5 = new DataBlob(dataCloudBlockBlob, _Engine, _Search, _Log).CalculateMD5Hash().ToString();
                        if (BlobMd5 == null)
                        {
                            _Log.LogInformation("\nWarning: Blob Hash calculation failed and will not be included in file information blob, continuing operation.");
                        }
                        else
                        {
                            //*****TODO***** update this to calculate the hash as the code looks to ppopulate the hash from what is either null or already correct...
                            dataCloudBlockBlob.Properties.ContentMD5 = BlobMd5;
                        }

                    }

                    //Get the content from the bound JSON file and instanciate a JsonBlob class then retrieve the labels collection from the Json to add to the image.
                    JsonBlob boundJson = new JsonBlob(bindingHash, _Engine, _Search, _Log);
                    //Note: you cannot pull the URL from the JSON blob because it will have the original URL from the first container when the blob was added to ML Professoar
                    string labeledDataUrl = dataCloudBlockBlob.StorageUri.PrimaryUri.ToString();
                    string evaluationDataParameterName = _Engine.GetEnvironmentVariable("evaluationDataParameterName", _Log);
                    string addLabeledDataParameters = $"?{evaluationDataParameterName }={labeledDataUrl}";
                    string labelingTagsParameterName = _Engine.GetEnvironmentVariable("labelingTagsParameterName", _Log);
                    //addLabeledDataParameters = $"{addLabeledDataParameters}&{labelingTagsParameterName}={trainingDataLabels}";
                    //string addLabeledDataContent = $"{labelingTagsParameterName}={trainingDataLabels}";

                    //construct and call model URL then fetch response
                    // the model always sends the label set in the message body with the name LabelsJson.  If your model needs other values in the URL then use
                    // {{environment variable name}}.
                    // So the example load labels function in the sameple model package would look like this:
                    // https://branddetectionapp.azurewebsites.net/api/loadimagetags/?projectID={{ProjectID}}
                    // The orchestration engine appends the labels json file to the message body.
                    // http://localhost:7071/api/LoadImageTags/?projectID=8d9d12d1-5d5c-4893-b915-4b5b3201f78e&labelsJson={%22Labels%22:[%22Hemlock%22,%22Japanese%20Cherry%22]}

                    string labeledDataServiceEndpoint = _Engine.GetEnvironmentVariable("LabeledDataServiceEndpoint", _Log);
                    string addLabeledDataUrl = _Engine.ConstructModelRequestUrl(labeledDataServiceEndpoint, addLabeledDataParameters);
                    _Log.LogInformation($"\n Getting response from {addLabeledDataUrl}");
                    //_Response = _Client.GetAsync(addLabeledDataUrl).Result;
                    _Response = await _Client.PostAsync(
                        addLabeledDataUrl,
                        new StringContent(boundJson.Labels, Encoding.UTF8, "application/json")
                        );
                    _ResponseString = _Response.Content.ReadAsStringAsync().Result;
                    if (string.IsNullOrEmpty(_ResponseString)) throw (new MissingRequiredObject($"\nresponseString not generated from URL: {addLabeledDataUrl}.  Processing will stop for labeleddata blobs."));

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

                    _Log.LogInformation($"Completed call to add blob: {dataCloudBlockBlob.Name} with labels: {JsonConvert.SerializeObject(boundJson.Labels)} to model.  The response string was: {_ResponseString}.");
                }
            }
            return "Completed execution of AddLabeledData.  See logs for success/fail details.";
        }

        public string LoadTrainingTags()
        {
            string responseString = "";

            //Construct a blob client to marshall the storage Functionality
            CloudStorageAccount storageAccount = _Engine.StorageAccount;
            CloudBlobClient labelsBlobClient = storageAccount.CreateCloudBlobClient();

            //Construct a blob storage container given a name string and a storage account
            string jsonDataContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
            CloudBlobContainer Container = labelsBlobClient.GetContainerReference(jsonDataContainerName);

            //get the training tags json blob from the container
            string labelingTagsBlobName = _Engine.GetEnvironmentVariable("labelingTagsBlobName", _Log);
            CloudBlockBlob dataTagsBlob = Container.GetBlockBlobReference(labelingTagsBlobName);

            //the blob has to be "touched" or the properties will all be null
            if (dataTagsBlob.Exists() != true)
            {
                throw new MissingRequiredObject($"\ndataTagsBlob not found using {labelingTagsBlobName}");
            };

            //get the environment variable specifying the MD5 hash of the last run tags file
            string lkgDataTagsFileHash = _Engine.GetEnvironmentVariable("dataTagsFileHash", _Log);

            //Check if there is a new version of the tags json file and if so load them into the environment
            if (dataTagsBlob.Properties.ContentMD5 != lkgDataTagsFileHash)
            {
                //format the http call to load labeling tags
                string labelingTags = dataTagsBlob.DownloadText(Encoding.UTF8);
                HttpContent labelingTagsContent = new StringContent(labelingTags);
                MultipartFormDataContent content = new MultipartFormDataContent();
                string labelingTagsParamatersName = _Engine.GetEnvironmentVariable("labelingTagsParameterName", _Log);
                content.Add(labelingTagsContent, labelingTagsParamatersName);

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                //string DataTagsUrl = DataTagsBlob.Uri.ToString(); //+ dataEvaluatingSas;

                //Make a request to the model service load labeling tags function passing the tags.
                string addLabelingTagsEndpoint = _Engine.GetEnvironmentVariable("TagsUploadServiceEndpoint", _Log);
                responseString = _Engine.GetHttpResponseString(addLabelingTagsEndpoint, content);
                if (string.IsNullOrEmpty(responseString)) throw (new MissingRequiredObject("\nresponseString not generated from URL: " + addLabelingTagsEndpoint));

                //save the hash of this version of the labeling tags file so that we can avoid running load labeling tags if the file has not changed.
                //*****TODO***** see if it is possible to persist the update using Powershell and passing in the name of the variable and the value as parameters
                //https://stackoverflow.com/questions/13251076/calling-powershell-from-c-sharp
                System.Environment.SetEnvironmentVariable("dataTagsFileHash", dataTagsBlob.Properties.ContentMD5);
                _Log.LogInformation(responseString);
            }
            return $"Training tags process executed with response: {responseString}";
        }

        public async Task<string> Train()
        {
            //Invoke the train model web service call
            string trainModelUrl = _Engine.GetEnvironmentVariable("TrainModelServiceEndpoint", _Log);
            _Response = _Client.GetAsync(trainModelUrl).Result;
            _ResponseString = _Response.Content.ReadAsStringAsync().Result;
            if (string.IsNullOrEmpty(_ResponseString)) throw (new MissingRequiredObject($"\nresponseString not generated from URL: {trainModelUrl}"));

            // Since a new model has been trained copy all of the pending new model blobs to pending evaluation
            CloudStorageAccount storageAccount = _Engine.StorageAccount;
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            string pendingNewModelStorageContainerName = _Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName", _Log);
            CloudBlobContainer pendingNewModelStorageContainer = blobClient.GetContainerReference(pendingNewModelStorageContainerName);
            string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
            CloudBlobContainer pendingEvaluationStorageContainer = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);

            foreach (IListBlobItem blob in pendingNewModelStorageContainer.ListBlobs(null, false))
            {
                if (blob.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob dataCloudBlockBlob = (CloudBlockBlob)blob;

                    //Hydrate Json Blob
                    JsonBlob jsonBlob = new JsonBlob(dataCloudBlockBlob.Properties.ContentMD5, _Engine, _Search, _Log);
                    JObject jsonBlobJObject = JObject.Parse(jsonBlob.AzureBlob.DownloadText());

                    // Add a state change too the Json Blob
                    JArray stateHistory = (JArray)jsonBlobJObject.SelectToken("StateHistory");
                    AddStateChange(pendingEvaluationStorageContainerName, stateHistory);

                    // Upload blob changes to the cloud
                    await _Engine.UploadJsonBlob(jsonBlob.AzureBlob, jsonBlobJObject);

                    // move blobs from pending neww model to pending evlaluation containers.
                    await _Engine.MoveAzureBlobToAzureBlob(storageAccount, dataCloudBlockBlob, pendingEvaluationStorageContainer.GetBlockBlobReference(dataCloudBlockBlob.Name));
                }
            }


            return _ResponseString;
        }

        public async Task TrainingProcess()
        {
            try
            {
                string responseString = "";
                string labeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
                CloudStorageAccount storageAccount = _Engine.StorageAccount;
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer labeledDataContainer = blobClient.GetContainerReference(labeledDataStorageContainerName);

                //*****TODO***** update this to check if there are any new files not just files
                // Check if there are any files in the labeled data container before loading labeling tags follwed by labeled data followed by training the model.
                if (labeledDataContainer.ListBlobs(null, false) != null)
                {
                    Search search = new Search(_Engine, _Log);

                    //Load the list of valid training tags to ensure all data labels are valid.
                    string loadTrainingTagsResult = LoadTrainingTags();

                    //Add full set set of labeled training data to the model
                    //*****TODO***** add logic to only add incremental labeled data to model
                    string addLabeledDataResult = await AddLabeledData();

                    //Train model using latest labeled training data.
                    string trainingResultsString = await Train();

                    //Construct response string for system logging.
                    responseString = $"\nModel training complete with the following result:" +
                        $"\nLoading Training Tags results: {loadTrainingTagsResult}" +
                        $"\nAdding Labeled Data results: {addLabeledDataResult}" +
                        $"\nTraining Results: {trainingResultsString}";
                }
                else
                {
                    throw (new MissingRequiredObject($"\n LabeledDataContainer was empty at {DateTime.Now} no model training action taken"));
                }
                _Log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}{responseString}");
            }
            catch (Exception e)
            {
                _Log.LogInformation($"\nError processing training timer: {e.Message}");
            }

        }

        public async Task<string> EvaluateData(string blobName)
        {
            try
            {
                double modelVerificationPercent = 0;
                string modelValidationStorageContainerName = "";

                string storageConnection = _Engine.GetEnvironmentVariable("AzureWebJobsStorage", _Log);
                string pendingEvaluationStorageContainerName = _Engine.GetEnvironmentVariable("pendingEvaluationStorageContainerName", _Log);
                string evaluatedDataStorageContainerName = _Engine.GetEnvironmentVariable("evaluatedDataStorageContainerName", _Log);
                string jsonStorageContainerName = _Engine.GetEnvironmentVariable("jsonStorageContainerName", _Log);
                string pendingSupervisionStorageContainerName = _Engine.GetEnvironmentVariable("pendingSupervisionStorageContainerName", _Log);
                string confidenceJsonPath = _Engine.GetEnvironmentVariable("confidenceJSONPath", _Log);
                double confidenceThreshold = Convert.ToDouble(_Engine.GetEnvironmentVariable("confidenceThreshold", _Log));

                string modelType = _Engine.GetEnvironmentVariable("modelType", _Log);
                if (modelType == "Trained")
                {
                    string labeledDataStorageContainerName = _Engine.GetEnvironmentVariable("labeledDataStorageContainerName", _Log);
                    modelValidationStorageContainerName = _Engine.GetEnvironmentVariable("modelValidationStorageContainerName", _Log);
                    string pendingNewModelStorageContainerName = _Engine.GetEnvironmentVariable("pendingNewModelStorageContainerName", _Log);
                    modelVerificationPercent = Convert.ToDouble(_Engine.GetEnvironmentVariable("modelVerificationPercentage", _Log));
                }

                //------------------------This section retrieves the blob needing evaluation and calls the evaluation service for processing.-----------------------

                // Create Reference to Azure Storage Account and the container for data that is pending evaluation by the model.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnection);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference(pendingEvaluationStorageContainerName);
                CloudBlockBlob rawDataBlob = container.GetBlockBlobReference(blobName);
                DataBlob dataEvaluating = new DataBlob(rawDataBlob, _Engine, _Search, _Log);
                if (dataEvaluating == null)
                {
                    throw (new MissingRequiredObject("\nMissing dataEvaluating blob object."));
                }

                //compute the file hash as this will be added to the meta data to allow for file version validation
                //the blob has to be "touched" or the properties will all be null
                if (dataEvaluating.AzureBlob.Exists() != true)
                {
                    throw new MissingRequiredObject($"\ndataEvaluating does not exist {dataEvaluating.AzureBlob.Name}");
                };

                string blobMd5 = _Engine.EnsureMd5(dataEvaluating);

                //****Currently only working with public access set on blob folders
                //Generate a URL with SAS token to submit to analyze image API
                //string dataEvaluatingSas = GetBlobSharedAccessSignature(dataEvaluating);
                string dataEvaluatingUrl = dataEvaluating.AzureBlob.Uri.ToString(); //+ dataEvaluatingSas;
                //string dataEvaluatingUrl = "test";

                //package the file contents to send as http request content
                //MemoryStream DataEvaluatingContent = new MemoryStream();
                //DataEvaluating.AzureBlob.DownloadToStreamAsync(DataEvaluatingContent);
                //HttpContent DataEvaluatingStream = new StreamContent(DataEvaluatingContent);
                var content = new MultipartFormDataContent();
                //content.Add(DataEvaluatingStream, "Name");

                //get environment variables used to construct the model request URL
                string dataEvaluationServiceEndpoint = _Engine.GetEnvironmentVariable("DataEvaluationServiceEndpoint", _Log);
                string evaluationDataParameterName = _Engine.GetEnvironmentVariable("evaluationDataParameterName", _Log);
                string parameters = $"?{evaluationDataParameterName}={dataEvaluatingUrl}";
                string evaluateDataUrl = _Engine.ConstructModelRequestUrl(dataEvaluationServiceEndpoint, parameters);

                int retryLoops = 0;
                string responseString = "";
                do
                {
                    //Make a request to the model service passing the file URL
                    responseString = _Engine.GetHttpResponseString(evaluateDataUrl, content);
                    //*****TODO***** "iteration" is a hard coded word that is specific to a model and needs to be a generic interface concept where the model must respond with an explicit success.
                    if (responseString.Contains("iteration"))
                    {
                        _Log.LogInformation($"\nEvaluation response: {responseString}.");
                        break;
                    }
                    retryLoops++;
                    await Task.Delay(1000);
                    if (retryLoops == 5)
                    {
                        _Log.LogInformation($"\nEvaluation of {evaluateDataUrl} failed 5 attempts with response: {responseString}");
                    }

                } while (retryLoops < 5);

                string strConfidence = null;
                double confidence = 0;
                JProperty responseProperty = new JProperty("Response", responseString);

                if (responseString == "Model not trained.")
                {
                    confidence = 0;
                }
                else
                {
                    //deserialize response JSON, get confidence score and compare with confidence threshold
                    JObject analysisJson = JObject.Parse(responseString);
                    try
                    {
                        strConfidence = (string)analysisJson.SelectToken(confidenceJsonPath);
                    }
                    catch
                    {
                        throw (new MissingRequiredObject($"\nInvalid response string {responseString} generated from URL: {evaluateDataUrl}."));
                    }

                    if (strConfidence == null)
                    {
                        //*****TODO***** if this fails the file will sit in the pending evaluation state because the trigger will have processed the file but the file could not be processed.  Need to figure out how to tell if a file failed processing so that we can reprocesses the file at a latter time.
                        throw (new MissingRequiredObject($"\nNo confidence value at {confidenceJsonPath} from environment variable ConfidenceJSONPath in response from model: {responseString}."));
                    }
                    confidence = (double)analysisJson.SelectToken(confidenceJsonPath);
                }

                //----------------------------This section collects information about the blob being analyzed and packages it in JSON that is then written to blob storage for later processing-----------------------------------

                _Log.LogInformation("\nStarting construction of json blob.");

                //create environment JSON object
                JProperty environmentProperty = _Engine.GetEnvironmentJson(_Log);
                JProperty evaluationPass = new JProperty("pass",
                    new JObject(
                            new JProperty("date", DateTime.Now),
                            environmentProperty,
                            new JProperty("request", evaluateDataUrl),
                            responseProperty
                        )
                    );

                //Note: all json files get writted to the same container as they are all accessed either by discrete name or by azure search index either GUID or Hash.
                CloudBlobContainer jsonContainer = blobClient.GetContainerReference(jsonStorageContainerName);
                CloudBlockBlob rawJsonBlob = jsonContainer.GetBlockBlobReference(_Engine.GetEncodedHashFileName(dataEvaluating.AzureBlob.Properties.ContentMD5.ToString()));

                // If the Json blob already exists then update the blob with latest pass iteration information
                if (rawJsonBlob.Exists())
                {
                    //Hydrate Json Blob
                    JsonBlob jsonBlob = new JsonBlob(blobMd5, _Engine, _Search, _Log);
                    JObject jsonBlobJObject = JObject.Parse(jsonBlob.AzureBlob.DownloadText());

                    // Add an evaluation pass to the Json blob
                    JArray evaluationHistory = (JArray)jsonBlobJObject.SelectToken("Passes");
                    AddEvaluationPass(evaluationPass, evaluationHistory);

                    // Upload blob changes to the cloud
                    await _Engine.UploadJsonBlob(jsonBlob.AzureBlob, jsonBlobJObject);
                }

                // If the Json blob does not exist create one and include the latest pass iteration information
                else
                {

                    JObject BlobAnalysis =
                        new JObject(
                            new JProperty("Id", Guid.NewGuid().ToString()),
                            new JProperty("IsDeleted", false),
                            new JProperty("Name", blobName),
                            new JProperty("Hash", blobMd5)
                        );

                    // Add state history information to Json blob
                    JArray stateChanges = new JArray();
                    AddStateChange(pendingEvaluationStorageContainerName, stateChanges);
                    JProperty stateHistory = new JProperty("StateHistory", stateChanges);
                    BlobAnalysis.Add(stateHistory);

                    // Add pass infromation to Json blob
                    JArray evaluations = new JArray();
                    AddEvaluationPass(evaluationPass, evaluations);
                    JProperty evaluationPasses = new JProperty("Passes", evaluations);
                    BlobAnalysis.Add(evaluationPasses);

                    CloudBlockBlob JsonCloudBlob = _Search.GetBlob(storageAccount, jsonStorageContainerName, _Engine.GetEncodedHashFileName(blobMd5));
                    JsonCloudBlob.Properties.ContentType = "application/json";

                    await _Engine.UploadJsonBlob(JsonCloudBlob, BlobAnalysis);
                }


                //--------------------------------This section processes the results of the analysis and transferes the blob to the container responsible for the next appropriate stage of processing.-------------------------------

                //model successfully analyzed content
                if (confidence >= confidenceThreshold)
                {
                    EvaluationPassed(modelVerificationPercent, modelValidationStorageContainerName, evaluatedDataStorageContainerName, storageAccount, dataEvaluating);
                }

                //model was not sufficiently confident in its analysis
                else
                {
                    EvaluationFailed(blobName, pendingSupervisionStorageContainerName, storageAccount, dataEvaluating);
                }

                _Log.LogInformation($"C# Blob trigger function Processed blob\n Name:{blobName}");
            }
            catch (MissingRequiredObject e)
            {
                _Log.LogInformation($"\n{blobName} could not be analyzed because of a MissingRequiredObject with message: {e.Message}");
            }
            catch (Exception e)
            {
                _Log.LogInformation($"\n{blobName} could not be analyzed with message: {e.Message}");
            }
            return $"Evaluate data completed evaluating data blob: {blobName}";
        }

        private static void AddEvaluationPass(JProperty evaluationPass, JArray evaluationHistory)
        {
            JObject evaluationsObject = new JObject
                    {
                        evaluationPass
                    };
            evaluationHistory.Add(evaluationsObject);
        }

        private static void AddStateChange(string newState, JArray stateHistory)
        {
            //Create state change property
            JProperty stateChange = new JProperty("StateChange",
                new JObject(
                    new JProperty("State", newState),
                    new JProperty("StateChangeDate", DateTime.Now)
                )
            );
            JObject stateHistoryObject = new JObject
                    {
                        stateChange
                    };
            stateHistory.Add(stateHistoryObject);
        }

        private async void EvaluationPassed(double modelVerificationPercent, string modelValidationStorageContainerName, string evaluatedDataStorageContainerName, CloudStorageAccount storageAccount, DataBlob dataEvaluating)
        {
            CloudBlockBlob evaluatedData = _Search.GetBlob(storageAccount, evaluatedDataStorageContainerName, dataEvaluating.AzureBlob.Name);
            if (evaluatedData == null)
            {
                throw (new MissingRequiredObject($"\nevaluatedData blob {dataEvaluating.AzureBlob.Name} destination blob not created in container {evaluatedDataStorageContainerName}"));
            }

            _Engine.CopyAzureBlobToAzureBlob(storageAccount, dataEvaluating.AzureBlob, evaluatedData).Wait();

            try
            {
                //Hydrate Json Blob
                JsonBlob jsonBlob = new JsonBlob(dataEvaluating.AzureBlob.Properties.ContentMD5, _Engine, _Search, _Log);
                JObject jsonBlobJObject = JObject.Parse(jsonBlob.AzureBlob.DownloadText());

                // Add a state change too the Json Blob
                JArray stateHistory = (JArray)jsonBlobJObject.SelectToken("StateHistory");
                AddStateChange(evaluatedDataStorageContainerName, stateHistory);

                // Upload blob changes to the cloud
                await _Engine.UploadJsonBlob(jsonBlob.AzureBlob, jsonBlobJObject);
            }
            catch
            {
                throw;
            }

            //pick a random number of successfully analyzed content blobs and submit them for supervision verification.
            Random rnd = new Random();
            if (Math.Round(rnd.NextDouble(), 2) <= modelVerificationPercent)
            {
                CloudBlockBlob modelValidation = _Search.GetBlob(storageAccount, modelValidationStorageContainerName, dataEvaluating.AzureBlob.Name);
                if (modelValidation == null)
                {
                    _Log.LogInformation($"\nWarning: Model validation skipped for {dataEvaluating.AzureBlob.Name} because {dataEvaluating.AzureBlob.Name} not created in destination blob in container {modelValidationStorageContainerName}");
                }
                else
                {
                    _Engine.CopyAzureBlobToAzureBlob(storageAccount, dataEvaluating.AzureBlob, modelValidation).Wait();
                }
            }
            await dataEvaluating.AzureBlob.DeleteIfExistsAsync();
        }

        private async void EvaluationFailed(string blobName, string pendingSupervisionStorageContainerName, CloudStorageAccount storageAccount, DataBlob dataEvaluating)
        {
            CloudBlockBlob pendingSupervision = _Search.GetBlob(storageAccount, pendingSupervisionStorageContainerName, blobName);
            if (pendingSupervision == null)
            {
                throw (new MissingRequiredObject($"\nMissing pendingSupervision {blobName} destination blob in container {pendingSupervisionStorageContainerName}"));
            }

            try
            {
                _Engine.MoveAzureBlobToAzureBlob(storageAccount, dataEvaluating.AzureBlob, pendingSupervision).Wait();
            }
            catch
            {
                throw;
            }

            //Hydrate Json Blob
            JsonBlob jsonBlob = new JsonBlob(dataEvaluating.AzureBlob.Properties.ContentMD5, _Engine, _Search, _Log);
            JObject jsonBlobJObject = JObject.Parse(jsonBlob.AzureBlob.DownloadText());

            // Add a state change too the Json Blob
            JArray stateHistory = (JArray)jsonBlobJObject.SelectToken("StateHistory");
            AddStateChange(pendingSupervisionStorageContainerName, stateHistory);

            // Upload blob changes to the cloud
            await _Engine.UploadJsonBlob(jsonBlob.AzureBlob, jsonBlobJObject);

        }
    }
}
