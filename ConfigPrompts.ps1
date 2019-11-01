while([string]::IsNullOrWhiteSpace($subscription))
  {$subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}


#######      variables for framework
$frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchestration framework for managing semisupervised models.  (default=MLProfessoar)'
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "MLProfessoar"}

$KeyVaultPrompt = "Input the name of the key vault that you want to create for this installation of the orchestration framework for managing semisupervised models.  (default=" + $frameworkResourceGroupName + "KeyVault)"
$frameworkKeyVaultName = Read-Host -Prompt $KeyVaultPrompt
if ([string]::IsNullOrWhiteSpace($frameworkKeyVaultName)) {$frameworkKeyVaultName = $frameworkResourceGroupName + "KeyVault"}

while([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
  {$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchestration framework.  Note this needs to be between 3 and 24 characters, globally unique in Azure, and contain all lowercase letters and or numbers.'
  if ($frameworkStorageAccountName.length -gt 24){$frameworkStorageAccountName=$null
    Write-Host "Storage account name cannot be longer than 24 charaters." -ForegroundColor "Red"}
  if (-Not ($modelStorageAccountName -cmatch "^[a-z0-9]*$")) {$frameworkStorageAccountName=$null
    Write-Host "Storage account name must not have upper case letters." -ForegroundColor "Red"}
  }

while([string]::IsNullOrWhiteSpace($frameworkFunctionAppName))
  {$frameworkFunctionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchestration framework.  Note this has to be unique across all of Azure.'}

$frameworkStorageAccountKey = $null #the script retrieves this at run time and populates it.

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
$pendingEvaluationStorageContainerName = "pendingevaluation"

$evaluatedDataStorageContainerName = "evaluateddata"

$jsonStorageContainerName = "json"

$pendingSupervisionStorageContainerName = "pendingsupervision"

$evaluationDataParameterName = "dataBlobUrl"

$labelsJsonPath = "labels.regions[0].tags"

$confidenceJSONPath = "confidence"

$confidenceThreshold = .95

$dataEvaluationServiceEndpoint = Read-Host -Prompt 'Input the http address of the evaluate data endpoint for your app. Note this has to match the endpoint of the model you have to will deploy.  It has to be unique across all of Azure. (default=https://mlpobjectdetectionapp.azurewebsites.net/api/EvaluateData)'
if ([string]::IsNullOrWhiteSpace($dataEvaluationServiceEndpoint)) {$dataEvaluationServiceEndpoint = 'https://mlpobjectdetectionapp.azurewebsites.net/api/EvaluateData'}

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
{
  $labeledDataStorageContainerName = "labeleddata"

  $modelValidationStorageContainerName = "modelvalidation"

  $pendingNewModelStorageContainerName = "pendingnewmodelevaluation"

  $modelVerificationPercentage = .05
  
  $trainModelServiceEndpoint = Read-Host -Prompt 'Input the http address of the services endpoint to initiate training of the model. Note this has to match the endpoint of the model you have to will deploy.  It has to be unique across all of Azure. (default=https://mlpobjectdetectionapp.azurewebsites.net/api/TrainModel)'
  if ([string]::IsNullOrWhiteSpace($trainModelServiceEndpoint)) {$trainModelServiceEndpoint = 'https://mlpobjectdetectionapp.azurewebsites.net/api/TrainModel'}

  $tagsUploadServiceEndpoint = Read-Host -Prompt 'Input the http address of the service endpoint to upload valid labeling tags to the model. Note this has to match the endpoint of the model you have to will deploy.  It has to be unique across all of Azure. (default=https://mlpobjectdetectionapp.azurewebsites.net/api/LoadLabelingTags)'
  if ([string]::IsNullOrWhiteSpace($tagsUploadServiceEndpoint)) {$tagsUploadServiceEndpoint = 'https://mlpobjectdetectionapp.azurewebsites.net/api/LoadLabelingTags'}

  $LabeledDataServiceEndpoint = Read-Host -Prompt 'Input the http address of the service endpoint to upload labeled data that will train the model. Note this has to match the endpoint of the model you have to will deploy.  It has to be unique across all of Azure. (default=https://mlpobjectdetectionapp.azurewebsites.net/api/AddLabeledData)'
  if ([string]::IsNullOrWhiteSpace($LabeledDataServiceEndpoint)) {$LabeledDataServiceEndpoint = 'https://mlpobjectdetectionapp.azurewebsites.net/api/AddLabeledData'}

  $LabelingSolutionName = Read-Host -Prompt 'Input the name of the labeling solution that will be used to label data. Valid Values: VoTT.  (default=VoTT)'
  if ([string]::IsNullOrWhiteSpace($LabelingSolutionName)) {$LabelingSolutionName = 'VoTT'}

  $labelingTagsParameterName = "labelsJson"
  
  $labelingTagsFileHash = 'hash not initialized'

  $labelingTagsBlobName = 'LabelingTags.json'

  $labelingOutputStorageContainerName = 'labelingoutput'

  while([string]::IsNullOrWhiteSpace($blobSearchServiceName))
  {$blobSearchServiceName = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. This name must be globally unique across Azure consist only of lowercase letters and dashes and cannot be longer than 60 characters.  (default="bindinghashsearch")'
  if ([string]::IsNullOrWhiteSpace($blobSearchServiceName)) {$blobSearchServiceName = 'bindinghashsearch'}
  if ($blobSearchServiceName.length -gt 60){$blobSearchServiceName=$null
    Write-Host "Search service name cannot be shorter than 2 characters and no longer than 60 charaters." -ForegroundColor "Red"}
  if ($blobSearchServiceName -cmatch '[A-Z]') {$blobSearchServiceName=$null
    Write-Host "Search service name must only contain lowercase letters, digits or dashes, cannot use dash as the first two or last one characters, cannot contain consecutive dashes, and is limited between 2 and 60 characters in length." -ForegroundColor "Red"}
  }

  $blobsearchdatasource = $blobSearchServiceName + "datasource"

  $blobSearchIndexName = $blobSearchServiceName + "index"

  $blobSearchEndpointUrl = "https://" + $blobSearchServiceName + ".search.windows.net"
}
