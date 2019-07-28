# Instructions and comments on using this solution have been moved to the Read Me file in the solution.

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
  --name $evaluatedJSONStorageContainerName `
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
if ($modelType -eq "Trained")
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
