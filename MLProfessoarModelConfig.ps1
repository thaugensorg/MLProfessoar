Param(
  [Parameter(Mandatory=$true)] [string] $subscription, 
  [Parameter(Mandatory=$true)] [string] $modelResourceGroupName,
  [Parameter(mandatory=$true)] [string] $modelLocation,
  [Parameter(mandatory=$true)] [string] $modelAppName,
  [Parameter(mandatory=$true)] [string] $modelStorageAccountName,
  [Parameter(mandatory=$true)] [string] $cognitiveServicesAccountName,
  [Parameter(mandatory=$true)] [string] $imageAnalysisEndpoint
)

while([string]::IsNullOrWhiteSpace($subscription)){
  $subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}

while([string]::IsNullOrWhiteSpace($modelLocation)){
  $modelLocation = Read-Host -Prompt 'Input the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.'}

while([string]::IsNullOrWhiteSpace($modelResourceGroupName)){
  $modelResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for this installation of the model.'}

#*****TODO***** enable "-" char in storage account name.
$validStorageAccountName = $false
$minLength = $false
$maxLength = $false
$charSet = $false
while(-not $validStorageAccountName){
  if ($modelStorageAccountName.length -lt 2){
    $modelStorageAccountName = Read-Host -Prompt "Storage account name $modelStorageAccountName cannot be shorter than 2 charaters"
  }else {$minLength = $true}
  if ($modelStorageAccountName.length -gt 24){
    $modelStorageAccountName = Read-Host -Prompt "Storage account name $modelStorageAccountName cannot be longer than 24 charaters"
  }else {$maxLength = $true}
  if (-Not ($modelStorageAccountName -cmatch "^[a-z0-9]*$")) {
    $modelStorageAccountName = Read-Host -Prompt "Storage account name $modelStorageAccountName can only contain lowercase letters, numbers, and -"
  }else {$charSet = $true}
  if ($minLength -and $maxLength -and $charSet){$validStorageAccountName = $true}
  }
  
while([string]::IsNullOrWhiteSpace($ModelAppName))
  {$ModelAppName= Read-Host -Prompt "Input the name for the azure function app you want to create for your analysis model. Note this must be a name that is unique across all of Azure"}

$modelStorageAccountKey = $null

if (az group exists --name $modelResourceGroupName) `
  {Write-Host "Deleting resource group." -ForegroundColor "Green" 
  az group delete `
	  --name $modelResourceGroupName `
	  --subscription $subscription `
	  --yes -y}

Write-Host "Creating Resource Group: " $modelResourceGroupName  -ForegroundColor "Green"

az group create `
  --name $modelResourceGroupName `
  --location $modelLocation 

Write-Host "Creating storage account: " $modelStorageAccountName  -ForegroundColor "Green"

az storage account create `
    --location $modelLocation `
    --name $modelStorageAccountName `
    --resource-group $modelResourceGroupName `
    --sku Standard_LRS

Write-Host "Getting storage account key." -ForegroundColor "Green"

$modelStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $modelResourceGroupName `
		-AccountName $modelStorageAccountName).Value[0]

Write-Host "Creating function app: " $ModelAppName -ForegroundColor "Green"

az functionapp create `
  --name $ModelAppName `
  --storage-account $modelStorageAccountName `
  --consumption-plan-location $modelLocation `
  --resource-group $modelResourceGroupName `
  --os-type "Linux" `
  --runtime "python"

Write-Host "Creating cognitive services account." $ModelAppName"Training" " and " $ModelAppName"Prediction" -ForegroundColor "Green"
Write-Host "Note: Azure custom vision is only available in a limited set of regions.  If you have selected aq region for your function app that is not supported by custom vision you will be prompted for a new location."    

if ("southcentralus", "westus2", "eastus", "eastus2", "northeurope", "westeurope", "southeastasia", "japaneast", "australiaeast", "centralindia", "uksouth","northcentralus" -contains $modelLocation) `
  {$modelCogServicesLocation = $modelLocation}
else
  {
    $modelCogServicesLocation = Read-Host -Prompt 'Input the Azure location, data center, where you want your cog services feature deployed.  Note, as of 8/29/19, custom vision features are only available in southcentralus, westus2, eastus, eastus2, northeurope, westeurope, southeastasia, japaneast, australiaeast, centralindia, uksouth, northcentralus.  (default=westus2)'
    if ([string]::IsNullOrWhiteSpace($modelCogServicesLocation)) {$modelCogServicesLocation = "westus2"}  
  }

$accountName = $ModelAppName + "Training"

az cognitiveservices account create `
    --name $accountName `
    --resource-group $modelResourceGroupName `
    --kind CustomVision.Training `
    --sku S0 `
    --location $modelCogServicesLocation `
    --yes

az cognitiveservices account create `
    --name $ModelAppName"Prediction" `
    --resource-group $modelResourceGroupName `
    --kind CustomVision.Prediction `
    --sku S0 `
    --location $modelCogServicesLocation `
    --yes

$cog_services_training_key = `
  (get-AzureRmCognitiveServicesAccountKey `
    -resourceGroupName $modelResourceGroupName `
    -AccountName $accountName).Key1

Write-Host "Creating app config setting: SubscriptionKey using account: $accountName and training key: $cog_services_training_key." -ForegroundColor "Green"

Write-Host "Creating app config setting: ProjectID for cognitive services." -ForegroundColor "Green"
Write-Host "Looking up existing projects.  It is OK if this errors as it simply means there are no projects."

$url = "https://$modelCogServicesLocation.api.cognitive.microsoft.com/customvision/v3.0/training/projects"
Write-Host "URL = $url"

  $headers = @{
      'Training-Key' = $cog_services_training_key }

$projects = (Invoke-RestMethod -Uri $url -Headers $headers -Method Get)
$projectName = ($projects | Where-Object {$_."name" -eq $accountName + "CustomVisionProject"} | Select-Object -Property name).name
$generatedProjectName = $ModelAppName + "CustomVisionProject"

if ($null -ne $projectName) {
  while($projectName -eq $generatedProjectName)
  {
    Write-Host "A cognitive services project already exists with the name: " $generatedProjectName -ForegroundColor "Red"

    $projectName= Read-Host -Prompt "Please enter a different cognitive services project name or delete the existing project with the same name using the Cognitive services portal."
  }
}
else {
  $projectName = $generatedProjectName
}

Write-Host "Creating cognitive services custom vision project: " $projectName -ForegroundColor "Green"

$retryCount = 0
$maxRetryCount = 3
$sleepLength = 5
while (-not $projectId -and $retryCount -lt $maxRetryCount) {
  Write-Host "Cog Services project create try $retryCount + 1 of $maxRetryCount"
  $url = "https://" + $modelCogServicesLocation + ".api.cognitive.microsoft.com/customvision/v3.0/training/projects?name=" + $projectName

  $headers = @{}
  $headers.add("Training-Key", $cog_services_training_key)
  $headers

  $url

  Invoke-RestMethod -Uri $url -Headers $headers -Method Post | ConvertTo-Json

  #get the project id to set as a config value
  $url = "https://westus2.api.cognitive.microsoft.com/customvision/v3.0/training/projects"

  $headers = @{
    'Training-Key' = $cog_services_training_key }

  $projects = (Invoke-RestMethod -Uri $url -Headers $headers -Method Get)
  $projects
  $projectId = ($projects | Where-Object {$_."name" -eq $projectName} | Select-Object -Property id).id
  $retryCount = $retryCount + 1
  Start-Sleep -s $sleepLength
  $sleepLength = $sleepLength + $sleepLength
}

$subscription_id = (Get-AzureRmSubscription -SubscriptionName Thaugen-semisupervised-vision-closed-loop-solution).Id
$resource_id = "/subscriptions/" + $subscription_id + "/resourceGroups/" + $modelResourceGroupName + "/providers/Microsoft.CognitiveServices/accounts/" + $accountName

az functionapp config appsettings set `
    --name $ModelAppName `
    --resource-group $modelResourceGroupName `
    --settings "ProjectID=$projectId " `
    "TrainingKey=$cog_services_training_key " `
    "ClientEndpoint=https://$modelCogServicesLocation.api.cognitive.microsoft.com/ " `
    "SubscriptionKey=$cog_services_training_key " `
    "ResourceID=$resource_id"

Write-Host "Creating app config setting: PredictionKey for cognitive services." -ForegroundColor "Green"

$accountName = $ModelAppName + "Prediction"

$cog_services_prediction_key = `
  (get-AzureRmCognitiveServicesAccountKey `
    -resourceGroupName $modelResourceGroupName `
    -AccountName $accountName).Key1

az functionapp config appsettings set `
    --name $ModelAppName `
    --resource-group $modelResourceGroupName `
    --settings "PredictionKey=$cog_services_prediction_key"
