<# this is a powershell script that will generate an Azure environment for the semisupervised framework.

Need to add confidenceThreshold, confidenceJSONPath, modelVerificationPercentage

It does not accept parameters yet, just set the variable values at the top to fit your needs.

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

$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

#variables for framework
$frameworkResourceGroupName = "semisupervisedFramework"
$frameworkStorageAccountName = "semisupervisedstorage"
$frameworkStorageAccountKey = $null
$frameworkLocation = "centralus" #get a list of all the locations and put a link to the web address here.

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
  --name semisupervisedApp `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

az storage container create `
  --name labeledtrainingdata `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey 

az storage container create `
  --name pendingsupervision `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name pendingevaluation `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name evaluateddata `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name modelvalidation`
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name pendingnewmodelevaluation
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az functionapp config appsettings set `
    --name semisupervisedApp
    --resource-group semisupervisedFramework
    --settings "modelType=packaged"

###### below starts the Image Analysis scripts ######

$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

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
  --name brandDetectionApp `
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
    --name brandDetectionApp
    --resource-group imageAnalysisModel
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

pip install pipreqs
pipreqs "C:\Users\thaugen\source\repos\brandDetectionModel"