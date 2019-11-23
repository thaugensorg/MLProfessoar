Param(
  [Parameter(Mandatory=$true)] [string] $subscription, 
  [Parameter(Mandatory=$true)] [string] $frameworkResourceGroupName,
  [Parameter(mandatory=$false)] [string] $frameworkKeyVaultName = $frameworkResourceGroupName + "KeyVault",
  [Parameter(mandatory=$false)] [string] $frameworkStorageAccountName = $frameworkResourceGroupName.ToLower() + "storage",
  [Parameter(mandatory=$false)] [string] $frameworkStorageAccountKey,        
  [Parameter(mandatory=$false)] [string] $frameworkFunctionAppName = $frameworkResourceGroupName + "App",
  [Parameter(mandatory=$true)] [string] $frameworkLocation,
  [Parameter(mandatory=$true)] [string] $modelType,
  [Parameter(mandatory=$false)] [string] $pendingEvaluationStorageContainerName = "pendingevaluation",
  [Parameter(mandatory=$false)] [string] $evaluatedDataStorageContainerName = "evaluateddata",
  [Parameter(mandatory=$false)] [string] $jsonStorageContainerName = "json",
  [Parameter(mandatory=$false)] [string] $pendingSupervisionStorageContainerName = "pendingsupervision",
  [Parameter(mandatory=$true)] [string] $evaluationDataParameterName,
  [Parameter(mandatory=$true)] [string] $labelsJsonPath,
  [Parameter(mandatory=$true)] [string] $confidenceJSONPath,
  [Parameter(mandatory=$true)] [string] $dataEvaluationServiceEndpoint,
  [Parameter(mandatory=$true)] [decimal] $confidenceThreshold,
  [Parameter(mandatory=$false)] [string] $labeledDataStorageContainerName = "labeleddata",
  [Parameter(mandatory=$false)] [string] $modelValidationStorageContainerName = "modelvalidation",
  [Parameter(mandatory=$false)] [string] $pendingNewModelStorageContainerName = "pendingnewmodelevaluation",
  [Parameter(mandatory=$true)] [decimal] $modelVerificationPercentage,
  [Parameter(mandatory=$true)] [string] $trainModelServiceEndpoint,
  [Parameter(mandatory=$true)] [string] $tagsUploadServiceEndpoint,
  [Parameter(mandatory=$true)] [string] $LabeledDataServiceEndpoint,
  [Parameter(mandatory=$true)] [string] $LabelingSolutionName,
  [Parameter(mandatory=$true)] [string] $labelingTagsParameterName,
  [Parameter(mandatory=$false)] [string] $labelingTagsFileHash = 'hash not initialized',
  [Parameter(mandatory=$false)] [string] $labelingTagsBlobName = 'LabelingTags.json',
  [Parameter(mandatory=$false)] [string] $blobSearchServiceName = 'bindinghashsearch',
  [Parameter(mandatory=$false)] [string] $labelingOutputStorageContainerName = 'labelingoutput')

# To Do: replace all azure CLI calls to PowerShell cmdlets such as get-azureRmStorageAccountKey

while([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
  {$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchestration framework.  Note this needs to be between 3 and 24 characters, globally unique in Azure, and contain all lowercase letters and or numbers.'
  if ($frameworkStorageAccountName.length -lt 2){$frameworkStorageAccountName=$null
    Write-Host "Storage account name cannot be shorter than 24 charaters." -ForegroundColor "Red"}
  if ($frameworkStorageAccountName.length -gt 24){$frameworkStorageAccountName=$null
    Write-Host "Storage account name cannot be longer than 24 charaters." -ForegroundColor "Red"}
  if (-Not ($frameworkStorageAccountName -cmatch "^[a-z0-9]*$")) {$frameworkStorageAccountName=$null
    Write-Host "Storage account name must not have upper case letters." -ForegroundColor "Red"}
  }

#*****TODO***** enable "-" char in function app name.
while([string]::IsNullOrWhiteSpace($frameworkFunctionAppName))
  {$frameworkFunctionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchestration framework.  Note this has to be unique across all of Azure.'
  if ($frameworkFunctionAppName.length -lt 2){$frameworkFunctionAppName=$null
    Write-Host "Function app name cannot be shorter than 2 charaters." -ForegroundColor "Red"}
  if ($frameworkFunctionAppName.length -gt 60){$frameworkFunctionAppName=$null
    Write-Host "Function app name cannot be longer than 60 charaters." -ForegroundColor "Red"}
  if (-Not ($frameworkFunctionAppName -cmatch "^[A-Za-z0-9]*$")) {$frameworkFunctionAppName=$null
    Write-Host "Function app name can only have numbers and upper / lower case letters." -ForegroundColor "Red"}
  }

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
{
  while([string]::IsNullOrWhiteSpace($blobSearchServiceName))
  {$blobSearchServiceName = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. This name must be globally unique across Azure consist only of lowercase letters and dashes and cannot be longer than 60 characters.'
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

Write-Host "frameworkFunctionAppName: " $frameworkFunctionAppName -ForegroundColor "Red"
Write-Host "frameworkKeyVaultName: " $frameworkKeyVaultName -ForegroundColor "Red"
$objectId = (Get-AzureADServicePrincipal -SearchString $frameworkFunctionAppName).ObjectId
Write-Host "objectId: " $objectId -ForegroundColor "Red"
Set-AzKeyVaultAccessPolicy -VaultName $frameworkKeyVaultName -ObjectId $objectId -PermissionsToSecrets Get

$staticStorageContainers = "$pendingEvaluationStorageContainerName $evaluatedDataStorageContainerName $pendingSupervisionStorageContainerName $jsonStorageContainerName testinvocation testdata" 
Write-Host "Creating static model storage containers: " $staticStorageContainers  -ForegroundColor "Green"
$staticStorageContainers.split() | New-AzStorageContainer -Context $StorageContext

#These storage containers are only used for trained models
if ($modelType -eq "Trained")
{
  $staticStorageContainers = "$pendingNewModelStorageContainerName $modelValidationStorageContainerName $labeledDataStorageContainerName $labelingOutputStorageContainerName" 
  Write-Host "Creating trained model storage containers: " $staticStorageContainers -ForegroundColor "Green"
  $staticStorageContainers.split() | New-AzStorageContainer -Context $StorageContext
}

#Search and labeling solution are only used with trained models
if ($modelType -eq "Trained")
{
  Write-Host "Creating blob binding hash search service: " $blobSearchServiceName -ForegroundColor "Green"

  New-AzSearchService `
      -ResourceGroupName $frameworkResourceGroupName `
      -Name $blobSearchServiceName `
      -Sku "Standard" `
      -Location $frameworkLocation `
      -PartitionCount 1 `
      -ReplicaCount 1 `
      -HostingMode Default

  Write-Host "Creating blob binding hash search service data source: " $blobsearchdatasource -ForegroundColor "Green"

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

$storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=mlprofessoarstorage;AccountKey=" + $frameworkStorageAccountKey + ";EndpointSuffix=core.windows.net"
$secretvalue = ConvertTo-SecureString $storageConnectionString -AsPlainText -Force
$secret = Set-AzKeyVaultSecret -VaultName $frameworkKeyVaultName -Name 'AzureWebJobsStorage' -SecretValue $secretvalue
$azureWebJobsStorageSetting = "@Microsoft.KeyVault(SecretUri=" + $secret.id + ") "

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
    "evaluationDataParameterName=$evaluationDataParameterName " `
    "AzureWebJobsStorage=$azureWebJobsStorageSetting"

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
