# Validate Parameters
# Resource Group Name
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName))
{
    $frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchestration framework for managing semisupervised models.  (default=MLProfessoar)'
    if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "MLProfessoar"}
}

# Storage Account Name
if ([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
{
    while([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
    {$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchestration framework.  Note this needs to be between 3 and 24 characters, globally unique in Azure, and contain all lowercase letters and or numbers.'
    if ($frameworkStorageAccountName.length -gt 24){$frameworkStorageAccountName=$null
        Write-Host "Storage account name cannot be longer than 24 charaters." -ForegroundColor "Red"}
    if ($frameworkStorageAccountName -cmatch '[A-Z]') {$frameworkStorageAccountName=$null
        Write-Host "Storage account name must not have upper case letters." -ForegroundColor "Red"}
    }
}

# Data Center Location
if ([string]::IsNullOrWhiteSpace($frameworkLocation))
{
    $frameworkLocation = Read-Host -Prompt 'Input the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.  (default=westus)'
    if ([string]::IsNullOrWhiteSpace($frameworkLocation)) {$frameworkLocation = "westus"}
}

# Json Storage Container Name
if ([string]::IsNullOrWhiteSpace($jsonStorageContainerName))
{
    $jsonStorageContainerName = Read-Host -Prompt 'Input the Azure location, data center, where you want this solution deployed.  Note, if you will be using Python functions as part of your solution, As of 8/1/19, Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and westus.  If you deploy your solution in a different data center network transit time may affect your solution performance.  (default=json)'
    if ([string]::IsNullOrWhiteSpace($jsonStorageContainerName)) {$jsonStorageContainerName = "json"}
}

# Model Type (trained or static
if ([string]::IsNullOrWhiteSpace($modelType))
{
    $title = "Input Model Type?"
    $message = "What type of model would you like to deploy?"
    $static = New-Object System.Management.Automation.Host.ChoiceDescription "&Static", "Static"
    $trained = New-Object System.Management.Automation.Host.ChoiceDescription "&Trained", "Trained"
    $options = [System.Management.Automation.Host.ChoiceDescription[]]($static, $trained)
    $type=$host.ui.PromptForChoice($title, $message, $options, 0)

    if ($type -eq 1) {$modelType = "Trained"} else {$modelType = "Static"}
}

# Search Service Name
if ([string]::IsNullOrWhiteSpace($blobSearchServiceName))
{
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

# Storage Account Key
$frameworkStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $frameworkResourceGroupName `
		-AccountName $frameworkStorageAccountName).Value[0]

# Storage Account Connection String
$connectionString = 'DefaultEndpointsProtocol=https;AccountName=' + $frameworkStorageAccountName + ';AccountKey=' + $frameworkStorageAccountKey + ';EndpointSuffix=core.windows.net'


#Search is only used with trained models
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
            "name": "id",
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
            "name": "hash",
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
            "name": "environment",
            "type": "Edm.ComplexType",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "endpoint",
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
            "name": "labels",
            "type": "Collection(Edm.String)",
            "facetable": false,
            "filterable": false,
            "retrievable": true,
            "searchable": false,
            "analyzer": null,
            "indexAnalyzer": null,
            "searchAnalyzer": null,
            "synonymMaps": [],
            "fields": []
          },
          {
            "name": "categories",
            "type": "Collection(Edm.ComplexType)",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "name",
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
                "name": "score",
                "type": "Edm.Double",
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
          },
          {
            "name": "color",
            "type": "Edm.ComplexType",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "dominantColorForeground",
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
                "name": "dominantColorBackground",
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
                "name": "dominantColors",
                "type": "Collection(Edm.String)",
                "facetable": false,
                "filterable": false,
                "retrievable": false,
                "searchable": false,
                "analyzer": null,
                "indexAnalyzer": null,
                "searchAnalyzer": null,
                "synonymMaps": [],
                "fields": []
              },
              {
                "name": "accentColor",
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
            "name": "description",
            "type": "Edm.ComplexType",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "tags",
                "type": "Collection(Edm.String)",
                "facetable": false,
                "filterable": false,
                "retrievable": false,
                "searchable": false,
                "analyzer": null,
                "indexAnalyzer": null,
                "searchAnalyzer": null,
                "synonymMaps": [],
                "fields": []
              },
              {
                "name": "captions",
                "type": "Collection(Edm.ComplexType)",
                "analyzer": null,
                "synonymMaps": [],
                "fields": [
                  {
                    "name": "text",
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
                    "name": "confidence",
                    "type": "Edm.Double",
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
            "name": "brands",
            "type": "Collection(Edm.ComplexType)",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "name",
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
                "name": "confidence",
                "type": "Edm.Double",
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
                "name": "rectangle",
                "type": "Edm.ComplexType",
                "analyzer": null,
                "synonymMaps": [],
                "fields": [
                  {
                    "name": "x",
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
                    "name": "y",
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
                    "name": "w",
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
                    "name": "h",
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
                  }
                ]
              }
            ]
          },
          {
            "name": "requestId",
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
            "name": "metadata",
            "type": "Edm.ComplexType",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "width",
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
                "name": "height",
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
                "name": "format",
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
            "key": true,
            "retrievable": true,
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
        "@odata.etag": "\"0x8D7226E5F8CFCA4\""
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
