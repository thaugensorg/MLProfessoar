# To Do: replace all azure CLI calls to PowerShell cmdlets such as get-azureRmStorageAccountKey

# Instructions and comments on using this solution have been moved to the Read Me file in the solution.

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

Write-Host "Creating key vault: " $frameworkKeyVaultName  -ForegroundColor "Green"

az keyvault create --name $frameworkKeyVaultName -g $frameworkResourceGroupName

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

$connectionString = 'DefaultEndpointsProtocol=https;AccountName=' + $frameworkStorageAccountName + ';AccountKey=' + $frameworkStorageAccountKey + ';EndpointSuffix=core.windows.net'
$StorageContext = New-AzureStorageContext -ConnectionString $connectionString

#enable CORS to support VoTT
if ($LabelingSolutionName -eq "VoTT")
{

  $CorsRules = (@{
    AllowedOrigins=@("*");
    AllowedMethods=@("Get");
    AllowedHeaders=@("*");
    ExposedHeaders=@("*"); 
    MaxAgeInSeconds=3600},
    @{
    AllowedOrigins=@("*");
    AllowedMethods=@("Post");
    AllowedHeaders=@("*");
    ExposedHeaders=@("*"); 
    MaxAgeInSeconds=3600},
    @{
    AllowedOrigins=@("*");
    AllowedMethods=@("Delete");
    AllowedHeaders=@("*");
    ExposedHeaders=@("*"); 
    MaxAgeInSeconds=3600},
    @{
    AllowedOrigins=@("*"); 
    AllowedMethods=@("Put")
    AllowedHeaders=@("*");
    ExposedHeaders=@("*"); 
    MaxAgeInSeconds=3600})

  Set-AzureStorageCORSRule -ServiceType Blob -Context $StorageContext -CorsRules $CorsRules
}

Write-Host "Creating function app: " $frameworkFunctionAppName -ForegroundColor "Green"

az functionapp create `
  --name $frameworkFunctionAppName `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

az webapp identity assign --name $frameworkFunctionAppName --resource-group $frameworkResourceGroupName

Set-AzKeyVaultAccessPolicy -VaultName $frameworkKeyVaultName -ObjectId (Get-AzureADServicePrincipal -SearchString $frameworkFunctionAppName).ObjectId -PermissionsToSecrets Get

$staticStorageContainers = "$pendingEvaluationStorageContainerName $evaluatedDataStorageContainerName $pendingSupervisionStorageContainerName $jsonStorageContainerName testinvocation testdata" 
Write-Host "Creating static model storage containers: " $staticStorageContainers  -ForegroundColor "Green"
$staticStorageContainers.split() | New-AzStorageContainer -Context $StorageContext

#These storage containers are only used for trained models
if ($modelType -eq "Trained")
{
  $staticStorageContainers = "$pendingNewModelStorageContainerName $modelValidationStorageContainerName $labeledDataStorageContainerName $labelingOutputStorageContainerName" 
  Write-Host "Creating trained model storage containers: " $staticStorageContainers  -ForegroundColor "Green"
  $staticStorageContainers.split() | New-AzStorageContainer -Context $StorageContext
}

#Search and labeling solution are only used with trained models
if ($modelType -eq "Trained")
{
  Write-Host "Creating blob binding hash search service: " $blobSearchServiceName  -ForegroundColor "Green"

  New-AzSearchService `
      -ResourceGroupName $frameworkResourceGroupName `
      -Name $blobSearchServiceName `
      -Sku "Standard" `
      -Location $frameworkLocation `
      -PartitionCount 1 `
      -ReplicaCount 1 `
      -HostingMode Default

  Write-Host "Creating blob binding hash search service data source: " $blobsearchdatasource  -ForegroundColor "Green"

  $blobSearchServiceKey = (Invoke-AzureRmResourceAction -Action listAdminKeys -ResourceType "Microsoft.Search/searchServices" -ResourceGroupName $frameworkResourceGroupName -ResourceName $blobSearchServiceName -ApiVersion 2015-08-19 -Force).primaryKey

  $url = "https://$blobSearchServiceName.search.windows.net/datasources/" + $blobsearchdatasource + "?api-version=2019-05-06"
  
  $headers = @{}
  $headers.add("Content-Type", "application/json")
  $headers.add("api-key", $blobSearchServiceKey)

  $body = @"
      {
          "name" : "$blobsearchdatasource",
          "type" : "azureblob",
          "credentials" : { "connectionString" : "$connectionString" },
          "container" : { "name" : "$jsonStorageContainerName" }
      }
"@

  Invoke-RestMethod -Uri $url -Headers $headers -Method Put -Body $body | ConvertTo-Json

  Write-Host "Creating blob binding hash search service index: " $blobSearchIndexName  -ForegroundColor "Green"

  $url = "https://$blobSearchServiceName.search.windows.net/indexes/" + $blobSearchIndexName + "?api-version=2019-05-06"

  $headers = @{
    'api-key' = "$blobSearchServiceKey"
    'Content-Type' = 'application/json' 
    'Accept' = 'application/json' }

  $body = @"
      {
        "name": "$blobSearchIndexName",
  "fields": [
    {
      "name": "Id",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": true,
      "retrievable": true,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "IsDeleted",
      "type": "Edm.Boolean",
      "facetable": false,
      "filterable": false,
      "retrievable": true,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "Name",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": true,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "Hash",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": true,
      "searchable": true,
      "sortable": false,
      "analyzer": "standard.lucene",
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "StateHistory",
      "type": "Collection(Edm.ComplexType)",
      "analyzer": null,
      "synonymMaps": [],
      "fields": [
        {
          "name": "StateChange",
          "type": "Edm.ComplexType",
          "analyzer": null,
          "synonymMaps": [],
          "fields": [
            {
              "name": "State",
              "type": "Edm.String",
              "facetable": false,
              "filterable": false,
              "key": false,
              "retrievable": false,
              "searchable": false,
              "sortable": false,
              "analyzer": null,
              "indexAnalyzer": null,
              "searchAnalyzer": null,
              "synonymMaps": [],
              "fields": []
            },
            {
              "name": "StateChangeDate",
              "type": "Edm.DateTimeOffset",
              "facetable": false,
              "filterable": false,
              "retrievable": false,
              "sortable": false,
              "analyzer": null,
              "indexAnalyzer": null,
              "searchAnalyzer": null,
              "synonymMaps": [],
              "fields": []
            }
          ]
        }
      ]
    },
    {
      "name": "Passes",
      "type": "Collection(Edm.ComplexType)",
      "analyzer": null,
      "synonymMaps": [],
      "fields": [
        {
          "name": "pass",
          "type": "Edm.ComplexType",
          "analyzer": null,
          "synonymMaps": [],
          "fields": [
            {
              "name": "date",
              "type": "Edm.DateTimeOffset",
              "facetable": false,
              "filterable": false,
              "retrievable": false,
              "sortable": false,
              "analyzer": null,
              "indexAnalyzer": null,
              "searchAnalyzer": null,
              "synonymMaps": [],
              "fields": []
            },
            {
              "name": "environment",
              "type": "Edm.ComplexType",
              "analyzer": null,
              "synonymMaps": [],
              "fields": [
                {
                  "name": "parameter",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "pendingEvaluationStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "evaluatedDataStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "pendingSupervisionStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "labeledDataStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "modelValidationStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "pendingNewModelStorage",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "confidenceJSONPath",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "confidenceThreshold",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                },
                {
                  "name": "verificationPercent",
                  "type": "Edm.String",
                  "facetable": false,
                  "filterable": false,
                  "key": false,
                  "retrievable": false,
                  "searchable": false,
                  "sortable": false,
                  "analyzer": null,
                  "indexAnalyzer": null,
                  "searchAnalyzer": null,
                  "synonymMaps": [],
                  "fields": []
                }
              ]
            },
            {
              "name": "request",
              "type": "Edm.String",
              "facetable": false,
              "filterable": false,
              "key": false,
              "retrievable": false,
              "searchable": false,
              "sortable": false,
              "analyzer": null,
              "indexAnalyzer": null,
              "searchAnalyzer": null,
              "synonymMaps": [],
              "fields": []
            },
            {
              "name": "Response",
              "type": "Edm.String",
              "facetable": false,
              "filterable": false,
              "key": false,
              "retrievable": false,
              "searchable": false,
              "sortable": false,
              "analyzer": null,
              "indexAnalyzer": null,
              "searchAnalyzer": null,
              "synonymMaps": [],
              "fields": []
            }
          ]
        }
      ]
    },
    {
      "name": "metadata_storage_content_type",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": false,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "metadata_storage_size",
      "type": "Edm.Int64",
      "facetable": false,
      "filterable": false,
      "retrievable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "metadata_storage_last_modified",
      "type": "Edm.DateTimeOffset",
      "facetable": false,
      "filterable": false,
      "retrievable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "metadata_storage_content_md5",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": false,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "metadata_storage_name",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": false,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    },
    {
      "name": "metadata_storage_path",
      "type": "Edm.String",
      "facetable": false,
      "filterable": false,
      "key": false,
      "retrievable": false,
      "searchable": false,
      "sortable": false,
      "analyzer": null,
      "indexAnalyzer": null,
      "searchAnalyzer": null,
      "synonymMaps": [],
      "fields": []
    }
  ],
  "suggesters": [],
  "scoringProfiles": [],
  "defaultScoringProfile": "",
  "corsOptions": null,
  "analyzers": [],
  "charFilters": [],
  "tokenFilters": [],
  "tokenizers": [],
  "@odata.etag": "\"0x8D73D620E7FA852\""
  }
"@

  Invoke-RestMethod -Uri $url -Headers $headers -Method Put -Body $body | ConvertTo-Json

  $blobSearchIndexerName = $blobSearchServiceName + "indexer"

  Write-Host "Creating blob binding hash search service indexer: " + $blobSearchIndexerName  -ForegroundColor "Green"

  $url = "https://$blobSearchServiceName.search.windows.net/indexers/" + $blobSearchIndexerName + "?api-version=2019-05-06"

  $headers = @{
      'Content-Type' = 'application/json'
      'api-key' = "$blobSearchServiceKey" }

  $body = @"
      {
        "name" : "$blobSearchIndexerName",
        "dataSourceName" : "$blobsearchdatasource",
        "targetIndexName" : "$blobSearchIndexName",
        "parameters" : { "maxFailedItems" : "15", "batchSize" : "100", "configuration" : { "parsingMode" : "json", "dataToExtract" : "contentAndMetadata" } },
        "fieldMappings" : [{"sourceFieldName" : "metadata_storage_path", "targetFieldName" : "metadata_storage_path", "mappingFunction" : { "name" : "base64Encode", "parameters" : { "useHttpServerUtilityUrlTokenEncode" : false }}}]
      }
"@

Invoke-RestMethod -Uri $url -Headers $headers -Method Put -Body $body | ConvertTo-Json

}

Write-Host "Creating app config settings." -ForegroundColor "Green"

# Create environment variables common to both static and trained models
az functionapp config appsettings set `
    --name $frameworkFunctionAppName `
    --resource-group $frameworkResourceGroupName `
    --settings "modelType=$modelType " `
    "pendingEvaluationStorageContainerName=$pendingEvaluationStorageContainerName " `
    "evaluatedDataStorageContainerName=$evaluatedDataStorageContainerName " `
    "jsonStorageContainerName=$jsonStorageContainerName " `
    "pendingSupervisionStorageContainerName=$pendingSupervisionStorageContainerName " `
    "labelsJsonPath=$labelsJsonPath " `
    "confidenceThreshold=$confidenceThreshold " `
    "confidenceJSONPath=$confidenceJSONPath " `
    "DataEvaluationServiceEndpoint=$dataEvaluationServiceEndpoint " `
    "evaluationDataParameterName=$evaluationDataParameterName"

# Create environment variables for trained models.
if ($modelType -eq "Trained")
{

Write-Host "Creating app config settings for trained models" -ForegroundColor "Green"

$secretvalue = ConvertTo-SecureString $blobSearchServiceKey -AsPlainText -Force
$secret = Set-AzKeyVaultSecret -VaultName $frameworkKeyVaultName -Name 'blobSearchKey' -SecretValue $secretvalue
$blobSearchKeySetting = "blobSearchKey=@Microsoft.KeyVault(SecretUri=" + $secret.id + ") "

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchEndpoint=$blobSearchEndpointUrl " `
  "labelingTagsParameterName=$labelingTagsParameterName " `
  "KeyVaultName=$frameworkKeyVaultName " `
  $blobSearchKeySetting `
  "blobSearchIndexName=$blobSearchIndexName " `
  "blobSearchServiceName=$blobSearchServiceName " `
  "blobsearchdatasource=$blobsearchdatasource " `
  "blobSearchIndexerName=$blobSearchIndexerName " `
  "labeledDataStorageContainerName=$labeledDataStorageContainerName " `
  "modelValidationStorageContainerName=$modelValidationStorageContainerName " `
  "pendingNewModelStorageContainerName=$pendingNewModelStorageContainerName " `
  "modelVerificationPercentage=$modelVerificationPercentage " `
  "labelingTagsBlobName=$labelingTagsBlobName " `
  "labelingTagsFileHash=$labelingTagsFileHash " `
  "TrainModelServiceEndpoint=$trainModelServiceEndpoint " `
  "TagsUploadServiceEndpoint=$tagsUploadServiceEndpoint " `
  "LabeledDataServiceEndpoint=$LabeledDataServiceEndpoint " `
  "labelingTagsBlobName=$labelingTagsBlobName " `
  "LabelingSolutionName=$LabelingSolutionName"
}
