<# this is a powershell script that will generate an Azure environment for the semisupervised framework.  It will prompt you for a number of parameters about the
environment.  It will help to plan your environment in advance.  Review this image as it will help you understand the structure of the solution https://1drv.ms/u/s!AvSAZXJ00YtnkdVVnMwKFBHLAIfqeg
then collect all of these values:
the name of the subscription where this solution will be deployed
the name of the resource group that you want to create for installing this orchistration framework for managing semisupervised models.  The default value is semisupervisedFramework
the name of the azure storage account you want to create for this installation of the orchistration framework.  By default this value is semisupervisedstorage
the name for the azure function app you want to create for this installation of the orchistration framework.  By default this value is semisupervisedApp
the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.  By default the solution deploys to westus.
the model type you want to deploy, static, meaning the framework does not support a training loop or trained which supports a full training loop
the name of the storage container for blobs to be evaluated by the model configured in this framework (default=pendingevaluation)
the name of the storage container for blobs after they are evaluated by the model (default=evaluateddata)
the name of the storage container for JSON blobs containing data generated from the blobs evaluated by this model (default=evaluatedjson)
the name of the storage container for blobs that require supervision after they have been evaluated by the model (default=pendingsupervision)

if you choose a trained model then you will need to have the following values also available:
the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)
the name of the storage container for blobs that will be used to validate the model after they have been evaluated by the model (default=modelvalidation)
the name of the storage container for blobs that need to be re-evaluated after a new mode has been published (default=pendingnewmodelevaluation)
the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)
the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=.05)
the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)

This does not yet deploy from source control, that will come in a future version.#>

<#First set up your python environemnt.  start by reading this document: https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-first-function-python
then this document: http://www.roelpeters.be/using-python-in-azure-functions/

You will need to install:
Python: https://www.python.org/downloads/windows/ note as of this writing, 7/15/19, azure functions only supports python 3.6.x so make sure and pick that version and if your OS is 64 bit pick the 64 bit version or it can create path issues
node.js: https://nodejs.org/en/download/
azure functions core tools: "npm install -g azure-functions-core-tools"
requests: "pip install requests" 
computer vision: "pip install azure-cognitiveservices-vision-customvision" for custom or "pip install azure-cognitiveservices-vision-computervision"

Then add these VS Code extenstions:
Python
Azure Account
Azure Functions


Then you will need to add these imports to the top of your python code in addition to the lines quick start added to your code:
import os
import json

from azure.cognitiveservices.vision.computervision import ComputerVisionClient
from azure.cognitiveservices.vision.computervision.models import VisualFeatureTypes
from msrest.authentication import CognitiveServicesCredentials

import requests
# If you are using a Jupyter notebook, uncomment the following line.
# %matplotlib inline
#import matplotlib.pyplot as plt
#from PIL import Image
from io import BytesIO

https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local

#>

while([string]::IsNullOrWhiteSpace($subscription))
  {$subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}

#variables for framework
$frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchistration framework for managing semisupervised models.  The default value is semisupervisedFramework'
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "semisupervisedFramework"}

$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchistration framework.  By default this value is semisupervisedstorage'
if ([string]::IsNullOrWhiteSpace($frameworkStorageAccountName)) {$frameworkStorageAccountName = "semisupervisedstorage"}

$functionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchistration framework.  By default this value is semisupervisedApp'
if ([string]::IsNullOrWhiteSpace($functionAppName)) {$functionAppName = "semisupervisedApp"}

$frameworkStorageAccountKey = $null #the script retrieves this at run time and populates it.
#these values we should try and get automatically and write to environment variables.
#string subscriptionKey = GetEnvironmentVariable("CognitiveServicesKey", log);

$frameworkLocation = Read-Host -Prompt 'Input the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.  By default the solution deploys to westus.'
if ([string]::IsNullOrWhiteSpace($frameworkLocation)) {$frameworkLocation = "westus"}

$title = "Input Model Type?"
$message = "What type of model would you like to deploy?"
$static = New-Object System.Management.Automation.Host.ChoiceDescription "&Static", "Static"
$trained = New-Object System.Management.Automation.Host.ChoiceDescription "&Trained", "Trained"
$options = [System.Management.Automation.Host.ChoiceDescription[]]($static, $trained)
$modelType=$host.ui.PromptForChoice($title, $message, $options, 0)

#These environment variables are used for both static and trained models.
$pendingEvaluationStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs to be evaluated by the model configured in this framework (default=pendingevaluation)'
if ([string]::IsNullOrWhiteSpace($pendingEvaluationStorageContainerName)) {$pendingEvaluationStorageContainerName = "pendingevaluation"}

$evaluatedDataStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs after they are evaluated by the model (default=evaluateddata)'
if ([string]::IsNullOrWhiteSpace($evaluatedDataStorageContainerName)) {$evaluatedDataStorageContainerName = "evaluateddata"}

$evaluatedJSONStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for JSON blobs containing data generated from the blobs evaluated by this model (default=evaluatedjson)'
if ([string]::IsNullOrWhiteSpace($evaluatedJSONStorageContainerName)) {$evaluatedJSONStorageContainerName = "evaluatedjson"}

$pendingSupervisionStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that require supervision after they have been evaluated by the model (default=pendingsupervision)'
if ([string]::IsNullOrWhiteSpace($pendingSupervisionStorageContainerName)) {$pendingSupervisionStorageContainerName = "pendingsupervision"}


#These environment variables are only used for trained models
if ($modelType == "Trained")
{
  $labeledDataStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)'
  if ([string]::IsNullOrWhiteSpace($labeledDataStorageContainerName)) {$labeledDataStorageContainerName = "labeleddata"}

  $modelValidationStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that will be used to validate the model after they have been evaluated by the model (default=modelvalidation)'
  if ([string]::IsNullOrWhiteSpace($modelValidationStorageContainerName)) {$modelValidationStorageContainerName = "modelvalidation"}

  $pendingNewModelStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that need to be re-evaluated after a new mode has been published (default=pendingnewmodelevaluation)'
  if ([string]::IsNullOrWhiteSpace($pendingNewModelStorageContainerName)) {$pendingNewModelStorageContainerName = "pendingnewmodelevaluation"}

  $confidenceThreshold = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)'
  if ([string]::IsNullOrWhiteSpace($confidenceThreshold)) {$confidenceThreshold = .95}

  $modelVerificationPercent = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=.05)'
  if ([string]::IsNullOrWhiteSpace($modelVerificationPercent)) {$modelVerificationPercent = .05}

  $confidenceJSONPath = Read-Host -Prompt 'Input the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)'
  if ([string]::IsNullOrWhiteSpace($confidenceJSONPath)) {$confidenceJSONPath = "confidence"}
}

if (az group exists --name $frameworkResourceGroupName) `
	{az group delete `
	  --name $frameworkResourceGroupName `
	  --subscription $subscription `
	  --yes -y}

az group create `
  --name $frameworkResourceGroupName `
  --location $frameworkLocation

az storage account create `
    --location $frameworkLocation `
    --name $frameworkStorageAccountName `
    --resource-group $frameworkResourceGroupName `
    --sku Standard_LRS

$frameworkStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $frameworkResourceGroupName `
		-AccountName $frameworkStorageAccountName).Value[0]

az functionapp create `
  --name $functionAppName `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

az storage container create `
  --name $pendingSupervisionStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name $pendingEvaluationStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name $evaluateddata `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name $evaluatedJSONStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

#These storage containers are only used for trained models
if ($modelType == "Trained")
{
  
  az storage container create `
    --name $labeledDataStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey 

  az storage container create `
    --name $modelValidationStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist

  az storage container create `
    --name $pendingNewModelStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist

}

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings $modelType

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingEvaluationStorageContainerName=" + $pendingEvaluationStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedDataStorageContainerName=" + $evaluatedDataStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedJSONStorageContainerName=" + $evaluatedJSONStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingSupervisionStorageContainerName=" + $pendingSupervisionStorageContainerName

az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceThreshold="$confidenceThreshold 

az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceJSONPath=description.captions[0].confidence"

az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelServiceEndpoint=https://branddetectionapp.azurewebsites.net/api/detectBrand/"

az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelAssetParameterName=name"

#These environment variables are only used for trained models
if ($modelType == "Trained")
{
az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labeledDataStorageContainerName=" + $labeledDataStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelValidationStorageContainerName=" + $modelValidationStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingNewModelStorageContainerName=" + $pendingNewModelStorageContainerName

az functionapp config appsettings set `
    --name $functionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelVerificationPercentage=" + $modelVerificationPercent
}

###### below starts the Image Analysis scripts ######

$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

$brandDetectionAppName = Read-Host -Prompt 'Input the name for the azure fucntion app you want to create for your analysis model.  By default this value is brandDetectionApp'
if ([string]::IsNullOrWhiteSpace($brandDetectionAppName)) {$brandDetectionAppName = "brandDetectionApp"}

$modelResourceGroupName = "imageAnalysisModel"
$modelStorageAccountName = "imageanalysisstorage"
$modelStorageAccountKey = $null
$modelLocation = "westus"

if (az group exists --name $modelResourceGroupName) `
	{az group delete `
	  --name $modelResourceGroupName `
	  --subscription $subscription `
	  --yes -y}

az group create `
  --name $modelResourceGroupName `
  --location $modelLocation 

az storage account create `
    --location $modelLocation `
    --name $modelStorageAccountName `
    --resource-group $modelResourceGroupName `
    --sku Standard_LRS

$modelStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $modelResourceGroupName `
		-AccountName $modelStorageAccountName).Value[0]

az functionapp create `
  --name $brandDetectionAppName `
  --storage-account $modelStorageAccountName `
  --consumption-plan-location $modelLocation `
  --resource-group $modelResourceGroupName `
  --os-type "Linux" `
  --runtime "python"

az cognitiveservices account create `
    --name "brandDetection" `
    --resource-group $modelResourceGroupName `
    --kind ComputerVision `
    --sku F0 `
    --location westus `
    --yes

az functionapp config appsettings set `
    --name $brandDetectionAppName `
    --resource-group imageAnalysisModel `
    --settings "subscriptionKey=Null"

#gitrepo=https://github.com/thaugensorg/semi-supervisedModelSolution.git
#token=<Replace with a GitHub access token>

# Enable authenticated git deployment in your subscription from a private repo.
#az functionapp deployment source update-token \
#  --git-token $token

# Create a function app with source files deployed from the specified GitHub repo.
#az functionapp create \
#  --name autoTestDeployment \
#  --storage-account semisupervisedstorage \
#  --consumption-plan-location centralUS\
#  --resource-group customVisionModelTest \
#  --deployment-source-url https://github.com/thaugensorg/semi-supervisedModelSolution.git \
#  --deployment-source-branch master

#pip install pipreqs
#pipreqs "C:\Users\thaugen\source\repos\brandDetectionModel"