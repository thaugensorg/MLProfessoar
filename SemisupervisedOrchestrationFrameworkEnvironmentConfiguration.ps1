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

$evaluationDataParameterName = Read-Host -Prompt 'Input the parameter name of the asset that will be passed into the azure function model (defaule=dataBlobURL)'
if ([string]::IsNullOrWhiteSpace($evaluationDataParameterName)) {$evaluationDataParameterName = "dataBlobUrl"}

$confidenceJSONPath = Read-Host -Prompt 'Input the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)'
if ([string]::IsNullOrWhiteSpace($confidenceJSONPath)) {$confidenceJSONPath = "confidence"}

$confidenceThreshold = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)'
if ([string]::IsNullOrWhiteSpace($confidenceThreshold)) {$confidenceThreshold = .95}

$dataEvaluationServiceEndpoint = Read-Host -Prompt 'Input the http address of the evaluate data endpoint for your app. (default="https://imagedetectionapp.azurewebsites.net/api/EvaluateData")'
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

  $modelVerificationPercentage = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=.05)'
  if ([string]::IsNullOrWhiteSpace($modelVerificationPercentage)) {$modelVerificationPercentage = .05}
  
  $labelingTagsBlobName = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=labelingTags)'
  if ([string]::IsNullOrWhiteSpace($labelingTagsBlobName)) {$labelingTagsBlobName = 'labelingTags'}

  $labelingTagsFileHash = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=hash not initialized)'
  if ([string]::IsNullOrWhiteSpace($labelingTagsFileHash)) {$labelingTagsFileHash = 'hash not initialized'}
  
  $trainModelServiceEndpoint = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=https://imagedetectionapp.azurewebsites.net/api/TrainModel)'
  if ([string]::IsNullOrWhiteSpace($trainModelServiceEndpoint)) {$trainModelServiceEndpoint = 'https://imagedetectionapp.azurewebsites.net/api/TrainModel'}

  $tagsUploadServiceEndpoint = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=https://imagedetectionapp.azurewebsites.net/api/LoadLabelingTags)'
  if ([string]::IsNullOrWhiteSpace($tagsUploadServiceEndpoint)) {$tagsUploadServiceEndpoint = 'https://imagedetectionapp.azurewebsites.net/api/LoadLabelingTags'}

  $labelingTagsBlobName = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated blobs to be routed to a verification queue (default=LabelingTags.json)'
  if ([string]::IsNullOrWhiteSpace($labelingTagsBlobName)) {$labelingTagsBlobName = 'LabelingTags.json'}

  $blobSearchEndpointUrl = Read-Host -Prompt 'Input the url that will be used to access the blob search service to locate JSON files bound to data (default=semisupervisedblobsearch)'
  if ([string]::IsNullOrWhiteSpace($blobSearchEndpointUrl)) {$blobSearchEndpointUrl = "semisupervisedblobsearch"}

  $blobSearchIndexName = Read-Host -Prompt 'Input the name of the index that will be used to access the blob binding hash. (default="bindinghash")'
  if ([string]::IsNullOrWhiteSpace($blobSearchIndexName)) {$blobSearchIndexName = "bindinghash"}

  $blobSearchServiceName = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. (default="semisupervisedblobsearch")'
  if ([string]::IsNullOrWhiteSpace($blobSearchServiceName)) {$blobSearchServiceName = "semisupervisedblobsearch"}
}

#########      settign up the Azure environment
if (az group exists --name $frameworkResourceGroupName) `
	{az group delete `
	  --name $frameworkResourceGroupName `
	  --subscription $subscription `
	  --yes -y}

Write-Host "Creating Resource Group: " $modelResourceGroupName  -ForegroundColor "Green"

az group create `
  --name $frameworkResourceGroupName `
  --location $frameworkLocation

  
Write-Host "Creating storage account: " $frameworkStorageAccountName  -ForegroundColor "Green"

az storage account create `
    --location $frameworkLocation `
    --name $frameworkStorageAccountName `
    --resource-group $frameworkResourceGroupName `
    --sku Standard_LRS

Write-Host "Getting storage account key." -ForegroundColor "Green"

$frameworkStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $frameworkResourceGroupName `
		-AccountName $frameworkStorageAccountName).Value[0]

Write-Host "Creating function app: " $frameworkFunctionAppName -ForegroundColor "Green"

az functionapp create `
  --name $frameworkFunctionAppName `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

Write-Host "Creating pending evaluation storage container: " $pendingEvaluationStorageContainerName  -ForegroundColor "Green"

az storage container create `
  --name $pendingEvaluationStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

Write-Host "Creating evaluated data storage container: " $evaluatedDataStorageContainerName  -ForegroundColor "Green"

az storage container create `
  --name $evaluatedDataStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

Write-Host "Creating pending supervision storage container: " $pendingSupervisionStorageContainerName  -ForegroundColor "Green"

az storage container create `
  --name $pendingSupervisionStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

Write-Host "Creating json storage container: " $jsonStorageContainerName  -ForegroundColor "Green"

az storage container create `
  --name $jsonStorageContainerName `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

#These storage containers are only used for trained models
if ($modelType -eq "Trained")
{
  
  Write-Host "Creating labeled data storage container: " $labeledDataStorageContainerName  -ForegroundColor "Green"

  az storage container create `
    --name $labeledDataStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist

  Write-Host "Creating model validation storage container: " $modelValidationStorageContainerName  -ForegroundColor "Green"

  az storage container create `
    --name $modelValidationStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist

  Write-Host "Creating pending new model storage container: " $pendingNewModelStorageContainerName  -ForegroundColor "Green"

  az storage container create `
    --name $pendingNewModelStorageContainerName `
    --account-name $frameworkStorageAccountName `
    --account-key $frameworkStorageAccountKey `
    --fail-on-exist
}

Write-Host "Creating app config setting: modelType: " $modelType -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelType=$modelType"

Write-Host "Creating app config setting: pendingEvaluationStorageContainerName: " $pendingEvaluationStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingEvaluationStorageContainerName=$pendingEvaluationStorageContainerName" 

Write-Host "Creating app config setting: evaluatedDataStorageContainerName: " $evaluatedDataStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "evaluatedDataStorageContainerName=$evaluatedDataStorageContainerName" 

Write-Host "Creating app config setting: jsonStorageContainerName: " $jsonStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "jsonStorageContainerName=$jsonStorageContainerName"

Write-Host "Creating app config setting: pendingSupervisionStorageContainerName: " $pendingSupervisionStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingSupervisionStorageContainerName=$pendingSupervisionStorageContainerName"

Write-Host "Creating app config setting: confidenceThreshold: " $confidenceThreshold -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceThreshold=$confidenceThreshold" 

Write-Host "Creating app config setting: confidenceJSONPath: " $confidenceJSONPath -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "confidenceJSONPath=$confidenceJSONPath"

Write-Host "Creating app config setting: modelServiceEndpoint: " $modelServiceEndpoint -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "modelServiceEndpoint=$modelServiceEndpoint"

Write-Host "Creating app config setting: evaluationDataParameterName: " $evaluationDataParameterName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "evaluationDataParameterName=$evaluationDataParameterName"

Write-Host "Creating app config setting: blobSearchEndpoint: " $frameworkFunctionAppName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchEndpoint=$blobSearchEndpointUrl"

Write-Host "Creating app config setting: blobSearchKey: null" -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchKey=null"

Write-Host "Creating app config setting: blobSearchIndexName: " $blobSearchIndexName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchIndexName=$blobSearchIndexName"

Write-Host "Creating app config setting: blobSearchIndexName: " $blobSearchServiceName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchIndexName=$blobSearchServiceName"

Write-Host "Creating app config setting: DataEvaluationServiceEndpoint: " $dataEvaluationServiceEndpoint -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "DataEvaluationServiceEndpoint=$dataEvaluationServiceEndpoint"

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
{
  Write-Host "Creating app config setting: labeledDataStorageContainerName: " $labeledDataStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labeledDataStorageContainerName=$labeledDataStorageContainerName"

Write-Host "Creating app config setting: modelValidationStorageContainerName: " $modelValidationStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelValidationStorageContainerName=$modelValidationStorageContainerName"

Write-Host "Creating app config setting: pendingNewModelStorageContainerName: " $pendingNewModelStorageContainerName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "pendingNewModelStorageContainerName=$pendingNewModelStorageContainerName"

Write-Host "Creating app config setting: modelVerificationPercentage: " $modelVerificationPercentage -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelVerificationPercentage=$modelVerificationPercentage"

Write-Host "Creating app config setting: labelingTagsBlobName: " $labelingTagsBlobName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labelingTagsBlobName=$labelingTagsBlobName"

Write-Host "Creating app config setting: labelingTagsFileHash: " $labelingTagsFileHash -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labelingTagsFileHash=$labelingTagsFileHash"

Write-Host "Creating app config setting: TrainModelServiceEndpoint: " $trainModelServiceEndpoint -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "TrainModelServiceEndpoint=$trainModelServiceEndpoint"

Write-Host "Creating app config setting: TagsUploadServiceEndpoint: " $tagsUploadServiceEndpoint -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "TagsUploadServiceEndpoint=$tagsUploadServiceEndpoint"

Write-Host "Creating app config setting: labelingTagsBlobName: " $labelingTagsBlobName -ForegroundColor "Green"

az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "labelingTagsBlobName=$labelingTagsBlobName"  
  }
