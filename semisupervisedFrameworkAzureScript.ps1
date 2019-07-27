<# This powershell script will generate an Azure environment for the semisupervised framework.  
Because this framework is dependent on having an analysis model to run, please deploy your analysis model 
before running this script, see the imageAnalysisModel.ps1 script in this directory.  It will save you 
configuration changes after you complete deployment of the framework portion of the solution.  You will 
be prompted for a significant number of parameters about the environment.  As a result, it will help to 
plan your environment in advance.  First, review this image as it will help you understand the structure 
of the solution https://1drv.ms/u/s!AvSAZXJ00YtnkdVVnMwKFBHLAIfqeg

Then collect all of these values:
- the name of the subscription where this solution will be deployed
- the name of the resource group that you want to create for installing this orchestration framework for 
managing semisupervised models (default = semisupervisedFramework)
- the name of the azure storage account you want to create for this installation of the orchestration 
framework (default = semisupervisedstorage)
- the name for the azure function app you want to create for this installation of the orchestration 
framework (default = semisupervisedApp)
- the Azure location, data center, where you want this solution deployed.  Note, if you will be using 
Python functions as part of your solution you must carefully choose your azure location, As of 8/1/19, 
Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and 
westus.  If you deploy your solution in a different data center network transit time may affect your 
solution performance (default = westus)
- the model type you want to deploy, static, meaning the framework does not support a training loop or 
trained which supports a full training loop.
- the name of the storage container for blobs to be evaluated by the model configured in this framework 
(default = pendingevaluation)
- the name of the storage container for blobs after they are evaluated by the model (default = evaluateddata)
- the name of the storage container for JSON blobs containing data generated from the blobs evaluated by 
this model (default = evaluatedjson)
- the name of the storage container for blobs that require supervision after they have been evaluated by 
the model (default = pendingsupervision)
- the JSON path where the blob analysis confidence value will be found in the JSON document found in the 
model analysis response.  By default, "confidence" is expected as a root key in the response JSON 
(default = confidence)
- the decimal value in the format of a C# Double that specifies the confidence threshold the model must return 
to indicate the model blob analysis is acceptable (default = .95)

if you choose to deploy a trained model then you will need to have the following values also available:
- the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)
- the name of the storage container for blobs that will be used to validate the model after they have been
evaluated by the model (default=modelvalidation)
- the name of the storage container for blobs that need to be re-evaluated after a new mode has been
published (default=pendingnewmodelevaluation)
- the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated 
blobs to be routed to a verification queue (default=.05)

This solution does not yet deploy from source control, that will come in a future version.

If you just want to run the packaged static azure vision service model then you are done once you complete
the deployment.  Simply upload and run both of the powershell scripts, .ps1 files, to your azure
subscription.  This article shows how to upload and run powershell scripts in Azure:
https://www.ntweekly.com/2019/05/24/upload-and-run-powershell-script-from-azure-cloud-shell/

#>

while([string]::IsNullOrWhiteSpace($subscription))
  {$subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}


#######      variables for framework
$frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchistration framework for managing semisupervised models.  The default value is semisupervisedFramework'
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "semisupervisedFramework"}

$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchistration framework.  By default this value is semisupervisedstorage'
if ([string]::IsNullOrWhiteSpace($frameworkStorageAccountName)) {$frameworkStorageAccountName = "semisupervisedstorage"}

$frameworkFunctionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchistration framework.  By default this value is semisupervisedApp'
if ([string]::IsNullOrWhiteSpace($frameworkFunctionAppName)) {$frameworkFunctionAppName = "semisupervisedApp"}

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

$modelServiceEndpoint = Read-Host -Prompt 'Input the URL (http address) of the model analysis function app'

$modelAssetParameterName = Read-Host -Prompt 'Input the parameter name of the asset that will be passed into the azure function model (defaule=name)'
if ([string]::IsNullOrWhiteSpace($modelAssetParameterName)) {$modelAssetParameterName = "name"}

$confidenceJSONPath = Read-Host -Prompt 'Input the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)'
if ([string]::IsNullOrWhiteSpace($confidenceJSONPath)) {$confidenceJSONPath = "confidence"}

$confidenceThreshold = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)'
if ([string]::IsNullOrWhiteSpace($confidenceThreshold)) {$confidenceThreshold = .95}

#These environment variables are only used for trained models
if ($modelType == "Trained")
{
  $labeledDataStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)'
  if ([string]::IsNullOrWhiteSpace($labeledDataStorageContainerName)) {$labeledDataStorageContainerName = "labeleddata"}

  $modelValidationStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that will be used to validate the model after they have been evaluated by the model (default=modelvalidation)'
  if ([string]::IsNullOrWhiteSpace($modelValidationStorageContainerName)) {$modelValidationStorageContainerName = "modelvalidation"}

  $pendingNewModelStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that need to be re-evaluated after a new mode has been published (default=pendingnewmodelevaluation)'
  if ([string]::IsNullOrWhiteSpace($pendingNewModelStorageContainerName)) {$pendingNewModelStorageContainerName = "pendingnewmodelevaluation"}

  $modelVerificationPercent = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=.05)'
  if ([string]::IsNullOrWhiteSpace($modelVerificationPercent)) {$modelVerificationPercent = .05}
}


#########      settign up the Azure environment
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
  --name $frameworkFunctionAppName `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

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
--name $pendingSupervisionStorageContainerName `
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
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelType=" + $modelType

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingEvaluationStorageContainerName=" + $pendingEvaluationStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedDataStorageContainerName=" + $evaluatedDataStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedJSONStorageContainerName=" + $evaluatedJSONStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingSupervisionStorageContainerName=" + $pendingSupervisionStorageContainerName

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceThreshold="$confidenceThreshold 

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceJSONPath=description.captions[0].confidence"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelServiceEndpoint=" + $modelServiceEndpoint

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelAssetParameterName=" + $modelAssetParameterName

#These environment variables are only used for trained models
if ($modelType == "Trained")
{
az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labeledDataStorageContainerName=" + $labeledDataStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelValidationStorageContainerName=" + $modelValidationStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingNewModelStorageContainerName=" + $pendingNewModelStorageContainerName

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelVerificationPercentage=" + $modelVerificationPercent
}
