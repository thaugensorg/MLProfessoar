# To Do: replace all azure CLI calls to PowerShell cmdlets such as get-azureRmStorageAccountKey

# Instructions and comments on using this solution have been moved to the Read Me file in the solution.

while([string]::IsNullOrWhiteSpace($subscription))
  {$subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}


#######      variables for framework
$frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchestration framework for managing semisupervised models.  (default=semisupervisedOrchestrationFramework'
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "semisupervisedOrchestrationFramework"}

while([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
  {$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchestration framework.  Note this needs to be between 3 and 24 characters, globally unique in Azure, and contain all lowercase letters and or numbers.'
  if ($frameworkStorageAccountName.length -gt 24){$frameworkStorageAccountName=$null
    Write-Host "Storage account name cannot be longer than 24 charaters." -ForegroundColor "Red"}
  if ($frameworkStorageAccountName -cmatch '[A-Z]') {$frameworkStorageAccountName=$null
    Write-Host "Storage account name must not have upper case letters." -ForegroundColor "Red"}
  }

$frameworkFunctionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchestration framework.  By default this value is semisupervisedApp'
if ([string]::IsNullOrWhiteSpace($frameworkFunctionAppName)) {$frameworkFunctionAppName = "semisupervisedApp"}

$frameworkStorageAccountKey = $null #the script retrieves this at run time and populates it.
#these values we should try and get automatically and write to environment variables.
#string subscriptionKey = GetEnvironmentVariable("CognitiveServicesKey", log);

$frameworkLocation = Read-Host -Prompt 'Input the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.  (default=westus)'
if ([string]::IsNullOrWhiteSpace($frameworkLocation)) {$frameworkLocation = "westus"}

$title = "Input Model Type?"
$message = "What type of model would you like to deploy?"
$static = New-Object System.Management.Automation.Host.ChoiceDescription "&Static", "Static"
$trained = New-Object System.Management.Automation.Host.ChoiceDescription "&Trained", "Trained"
$options = [System.Management.Automation.Host.ChoiceDescription[]]($static, $trained)
$type=$host.ui.PromptForChoice($title, $message, $options, 0)

if ($type -eq 1) {$modelType = "Trained"} else {$modelType = "Static"}

#These environment variables are used for both static and trained models.
$pendingEvaluationStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs to be evaluated by the model configured in this framework (default=pendingevaluation)'
if ([string]::IsNullOrWhiteSpace($pendingEvaluationStorageContainerName)) {$pendingEvaluationStorageContainerName = "pendingevaluation"}

$evaluatedDataStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs after they are evaluated by the model (default=evaluateddata)'
if ([string]::IsNullOrWhiteSpace($evaluatedDataStorageContainerName)) {$evaluatedDataStorageContainerName = "evaluateddata"}

$jsonStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for JSON blobs containing data generated from the blobs evaluated by this model (default=json)'
if ([string]::IsNullOrWhiteSpace($jsonStorageContainerName)) {$jsonStorageContainerName = "json"}

$pendingSupervisionStorageContainerName = Read-Host -Prompt 'Input the name of the storage container for blobs that require supervision after they have been evaluated by the model (default=pendingsupervision)'
if ([string]::IsNullOrWhiteSpace($pendingSupervisionStorageContainerName)) {$pendingSupervisionStorageContainerName = "pendingsupervision"}

$modelServiceEndpoint = Read-Host -Prompt 'Input the URL (http address) of the model analysis function app'

$modelAssetParameterName = Read-Host -Prompt 'Input the parameter name of the asset that will be passed into the azure function model (defaule=name)'
if ([string]::IsNullOrWhiteSpace($modelAssetParameterName)) {$modelAssetParameterName = "name"}

$confidenceJSONPath = Read-Host -Prompt 'Input the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)'
if ([string]::IsNullOrWhiteSpace($confidenceJSONPath)) {$confidenceJSONPath = "confidence"}

$confidenceThreshold = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)'
if ([string]::IsNullOrWhiteSpace($confidenceThreshold)) {$confidenceThreshold = .95}

$blobSearchEndpointUrl = Read-Host -Prompt 'Input the url that will be used to access the blob search service to locate JSON files bound to data (default=.semisupervisedblobsearch)'
if ([string]::IsNullOrWhiteSpace($blobSearchEndpointUrl)) {$blobSearchEndpointUrl = "semisupervisedblobsearch"}

$blobSearchIndexName = Read-Host -Prompt 'Input the name of the index that will be used to access the blob binding hash. (default="bindinghash")'
if ([string]::IsNullOrWhiteSpace($blobSearchIndexName)) {$blobSearchIndexName = "bindinghash"}

$blobSearchServiceName = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. (default="semisupervisedblobsearch")'
if ([string]::IsNullOrWhiteSpace($blobSearchServiceName)) {$blobSearchServiceName = "semisupervisedblobsearch"}

$dataEvaluationServiceEndpoint = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. (default="https://imagedetectionapp.azurewebsites.net/api/EvaluateData")'
if ([string]::IsNullOrWhiteSpace($dataEvaluationServiceEndpoint)) {$dataEvaluationServiceEndpoint = "https://imagedetectionapp.azurewebsites.net/api/EvaluateData"}

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
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
  --name $evaluatedDataStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name $pendingSupervisionStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name $jsonStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

#These storage containers are only used for trained models
if ($modelType -eq "Trained")
{
  
  az storage container create `
    --name $labeledDataStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist

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
    --settings "modelType=$modelType"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingEvaluationStorageContainerName=$pendingEvaluationStorageContainerName" 

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedDataStorageContainerName=$evaluatedDataStorageContainerName" 

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "jsonStorageContainerName=$jsonStorageContainerName"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingSupervisionStorageContainerName=$pendingSupervisionStorageContainerName"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceThreshold=$confidenceThreshold" 

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceJSONPath=$confidenceJSONPath"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelServiceEndpoint=$modelServiceEndpoint"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelAssetParameterName=$modelAssetParameterName"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchEndpoint=$blobSearchEndpointUrl"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchKey=null"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchIndexName=$blobSearchIndexName"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchIndexName=$blobSearchServiceName"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "DataEvaluationServiceEndpoint=$dataEvaluationServiceEndpoint"

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
{
az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labeledDataStorageContainerName=$labeledDataStorageContainerName"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelValidationStorageContainerName=$modelValidationStorageContainerName"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingNewModelStorageContainerName=$pendingNewModelStorageContainerName"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelVerificationPercentage=$modelVerificationPercent"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "dataTagsBlobName=labels.json"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "dataTagsFileHash=null"
}
