# Welcome to ML Professoar

**M**achine
**L**earning
**Pr**ocess
**O**rchestration
**F**or
**E**nabling
**S**emisupervised
**S**ervices
**O**n
**A**zure
**R**esources

FYI, this repo is currently not functioning.  Azure changed the deployment interfaces and I have not had time to fix them yet.  When I fix them I will also port to GraphQL and organize teh connected read me files better.

First, review the [architecture image](https://github.com/thaugensorg/semisupervisedFramework/blob/master/Architecture.jpg) included with the solution as it will help you understand the structure of the solution.

# ML Professoar
This solution configures an Azure subscription to enable automated orchestation of semi-supervised AI/ML solutions.  A semi-supervised solution will have two components, the engine, covered in this readme and a model provided by you.  The model is generally a custom model specific to the ML problem but Azure provides three "packaged" models that have been integrated with ML Professoar by way of example.  There are two versions of the engine that can be deployed, static and trained.  A static model cannot be trained but can still have evaluations that do not meet the confidence threshold required by your solution vs a trained model which will have a labeling stage allowing the capabilities of your model to improve over time through trainign based on the labels.  The ML Professoar solution handels all of the invocation of the training and models as well as the management of the contant and its associated labeling required to operationalize a semi-supervised ML solution.  Data Scientists using this model are required to have minimal knowledge of Azure and the code required to orchestrate a model on Azure.  Models simply have to be invocable via HTTP and respond with JSON.  The interface beyond that is fully configurable such that it often works with existing models with little or no changes to the existing model.

## Dependencies
ML Professoar is dependent on an externalized model, the model must be invocable via http that accepts a file URL and returns JSON content in the body of the response where a single value in the JSON represents confidence in the model analysis.  All interface definition is via environment configuration values so the address, parameter names, and JSON path are not hardcoded dependencies for this project but are configurable through Azure Functions.  You can find a static model sample at [Brand Detection Model](https://github.com/thaugensorg/brandDetectionModel) and a trained model sample at [Semisupervised Data EvaluationModel](https://github.com/thaugensorg/SemisupervisedDataEvaluationModel)

Additionally, the project requires the Azure Search CLI installed.  From your Cloud Shell run "Install-Module az.search" accept the warning prompt.  Once it installs you will be able to run the PS1 script to configure the ML Professoar Azure environment.

## Getting started
First, review the [architecture image](https://github.com/thaugensorg/semisupervisedFramework/blob/master/Architecture.jpg) included with the solution as it will help you understand the structure of the solution.

To get started, install azure search module for powershell, install-module az.search, and then save the powershell, MLProfessoarEngienConfig.ps1, script to your environment, see deployment below.  This is the script that will configure your azure environment for ML Professoar.  Because this framework is dependent on having an analysis model to run, please deploy your analysis model before running this script, see the SemisupervisedDataEvaluationModelEnvironmentConfiguration.ps1 script in the semisupervised data evaluation model project.  It will save you additional configuration steps after you complete deployment.

The PowerShell script will prompt for a significant number of parameters about the environment.  As a result, it will help to plan your environment in advance.  See the Configuration Parameters below for the values the scripts require.

Note: if the script errors simply run it again.  Enter the same name for the resource group and the script will clean up the previous attempt as the first step. When the script checks for the resource group if it does not find it the script will out put a red message resource group not found.  This is OK, it simply means that the script needs to create the resource group. 

## Deploying to Azure
This article shows how to upload and run powershell scripts in Azure:
[PowerShell in Azure](https://www.ntweekly.com/2019/05/24/upload-and-run-powershell-script-from-azure-cloud-shell/)

To deploy this project to the cloud after you have the PowerShell script open the project in Visual Studio, then open Cloud Explorer in Visual Studio and sign into Azure, then right click on your function app that you configured in the ps1 script and select deploy.

## Configuration Parameters
Then collect all of these values:
- the name of the subscription where this solution will be deployed
- the name of the resource group that you want to create for installing this orchestration framework for managing semisupervised models (default = semisupervisedFramework)
- the name of the azure storage account you want to create for this installation of the orchestration framework
- the name for the azure function app you want to create for this installation of the orchestration framework
- the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution you must carefully choose your azure location, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance (default = westus)
- the model type you want to deploy:
    'static', meaning the framework does not support a training loop in the model
  or 
    'trained' which supports a model with a full training loop
- the name of the storage container for blobs to be evaluated by the model configured in this framework (default = pendingevaluation)
- the name of the storage container for blobs after they are evaluated by the model (default = evaluateddata)
- the name of the storage container for JSON blobs containing data generated from the blobs evaluated by this model (default = evaluatedjson)
- the name of the storage container for blobs that require supervision after they have been evaluated by the model (default = pendingsupervision)
- the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default, "confidence" is expected as a root key in the response JSON (default = confidence)
- the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default = .95)

if you choose to deploy a trained model then you will need to have the following values also available:
- the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)
- the name of the storage container for blobs that will be used to validate the model after they have been evaluated by the model (default=modelvalidation)
- the name of the storage container for blobs that need to be re-evaluated after a new mode has been published (default=pendingnewmodelevaluation)
- the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=.05)

## Run it locally
To run this project locally, once you have run the ps1 script open the project in Visual Studio and set up your local.settings.json file.  It must have the following values:
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "{replace with your storage account end point}",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet", {this must stay as dotnet}
    "modelType": "static",
    "confidenceJSONPath": "{replace with the JSON path to the confidence value in the http response.}",
    "confidenceThreshold": "0.95", {must be a double value that represents the cut off for requireing supervision}
    "modelVerificationPercentage": ".05", {must be a double value that represents the percenatage of files that will be validated}
    "modelServiceEndpoint": "{replace with your model URL root eg:"https://branddetectionapp.azurewebsites.net/api/detectBrand/}",
    "modelAssetParameterName": "{replace with the models parameter name that will contain the file URL}"
  }
}

Get your storage account end point by navigating to your storage account in https://portal.azure.com then click on access keys in the left hand navigation pane and select 'Access Keys'.  Copy the connection string then paste it into the AzureWebJobsStorage value.

- Fill in either 'static' or 'trained' Model Type value that you configured in the ps1 script.
- Add the confidence value path you entered in your ps1 script.
- Add a confidence threshold and verification percentage
- Finally add the root heep address for your model and the name of the parameter the model expects to contain the file name.

## Testing
To run the test framework for this project copy the TestData directory to your Azure storage account using storage explorer, this should include all fiels and subsirectories.  Start testing by either running the project locally or having the functions deployed to your Azure Function App.  Once you have the functions deployed and running simply copy the appropriate test command from your local TestCommands directory in your local repo to the testinvocation container in your azure storage account.  Normally you will simply use the TestAll.test blob to test the full ML Professoar framework.  Placing this blob in the testinvocation container will launch the TestTrigger Azure Function Blob Trigger in the project.  The results of the test will be saved in a blob in the testinvocation container with the name TestResult-GUID.txt.  Use the date time stamp on the file to determine which file corresponds to your latest test run.

# Model Interface
## Evaluate
Accepts a URL and returns JSON with a value that equals the confidence of the evaluation.

## Load Tags
Accepts a set of tags as URL paramters.

## Load Training Data
Accepts an image with its associated tags.  Is called once per training image.

## Train
Does not accept input parameters returns success/fail.

By participating in this project, you
agree to abide by the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/)
