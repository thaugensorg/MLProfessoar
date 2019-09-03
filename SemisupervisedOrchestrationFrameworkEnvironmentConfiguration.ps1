# To Do: replace all azure CLI calls to PowerShell cmdlets such as get-azureRmStorageAccountKey

# Instructions and comments on using this solution have been moved to the Read Me file in the solution.

while([string]::IsNullOrWhiteSpace($subscription))
  {$subscription= Read-Host -Prompt "Input the name of the subscription where this solution will be deployed"}


#######      variables for framework
$frameworkResourceGroupName = Read-Host -Prompt 'Input the name of the resource group that you want to create for installing this orchestration framework for managing semisupervised models.  (default=MLProfessoar)'
if ([string]::IsNullOrWhiteSpace($frameworkResourceGroupName)) {$frameworkResourceGroupName = "MLProfessoar"}

while([string]::IsNullOrWhiteSpace($frameworkStorageAccountName))
  {$frameworkStorageAccountName = Read-Host -Prompt 'Input the name of the azure storage account you want to create for this installation of the orchestration framework.  Note this needs to be between 3 and 24 characters, globally unique in Azure, and contain all lowercase letters and or numbers.'
  if ($frameworkStorageAccountName.length -gt 24){$frameworkStorageAccountName=$null
    Write-Host "Storage account name cannot be longer than 24 charaters." -ForegroundColor "Red"}
  if ($frameworkStorageAccountName -cmatch '[A-Z]') {$frameworkStorageAccountName=$null
    Write-Host "Storage account name must not have upper case letters." -ForegroundColor "Red"}
  }

$frameworkFunctionAppName = Read-Host -Prompt 'Input the name for the azure function app you want to create for this installation of the orchestration framework.  By default this value is MLProfessoarApp'
if ([string]::IsNullOrWhiteSpace($frameworkFunctionAppName)) {$frameworkFunctionAppName = "MLProfessoarApp"}

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

$evaluationDataParameterName = Read-Host -Prompt 'Input the parameter name of the asset that will be passed into the azure function model (defaule=dataBlobURL)'
if ([string]::IsNullOrWhiteSpace($evaluationDataParameterName)) {$evaluationDataParameterName = "dataBlobUrl"}

$confidenceJSONPath = Read-Host -Prompt 'Input the JSON path where the blob analysis confidence value will be found in the JSON document found in the model analysis response.  By default confidence is expected as a root value in the response JSON (default=confidence)'
if ([string]::IsNullOrWhiteSpace($confidenceJSONPath)) {$confidenceJSONPath = "confidence"}

$confidenceThreshold = Read-Host -Prompt 'Input the decimal value in the format of a C# Double that specifies the confidence threshold the model must return to indicate the model blob analysis is acceptable (default=.95)'
if ([string]::IsNullOrWhiteSpace($confidenceThreshold)) {$confidenceThreshold = .95}

$dataEvaluationServiceEndpoint = Read-Host -Prompt 'Input the http address of the evaluate data endpoint for your app. (default="https://mlprofessoarsamplemodelapp.azurewebsites.net/api/EvaluateData")'
if ([string]::IsNullOrWhiteSpace($dataEvaluationServiceEndpoint)) {$dataEvaluationServiceEndpoint = "https://mlprofessoarsamplemodelapp.azurewebsites.net/api/EvaluateData"}

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

  $labelingTagsFileHash = Read-Host -Prompt 'This environment variable cannot be set at configuration time. (default=hash not initialized)'
  if ([string]::IsNullOrWhiteSpace($labelingTagsFileHash)) {$labelingTagsFileHash = 'hash not initialized'}
  
  $trainModelServiceEndpoint = Read-Host -Prompt 'Input the http address of the services endpoint to initiate training of the model. (default=https://mlprofessoarsamplemodelapp.azurewebsites.net/api/TrainModel)'
  if ([string]::IsNullOrWhiteSpace($trainModelServiceEndpoint)) {$trainModelServiceEndpoint = 'https://mlprofessoarsamplemodelapp.azurewebsites.net/api/TrainModel'}

  $tagsUploadServiceEndpoint = Read-Host -Prompt 'Input the http address of the service endpoint to upload valid labeling tags to the model. (default=https://mlprofessoarsamplemodelapp.azurewebsites.net/api/LoadLabelingTags)'
  if ([string]::IsNullOrWhiteSpace($tagsUploadServiceEndpoint)) {$tagsUploadServiceEndpoint = 'https://mlprofessoarsamplemodelapp.azurewebsites.net/api/LoadLabelingTags'}

  $LabeledDataServiceEndpoint = Read-Host -Prompt 'Input the http address of the service endpoint to upload labeled data that will train the model. (default=https://mlprofessoarsamplemodelapp.azurewebsites.net/api/AddLabeledDataClient)'
  if ([string]::IsNullOrWhiteSpace($LabeledDataServiceEndpoint)) {$LabeledDataServiceEndpoint = 'https://mlprofessoarsamplemodelapp.azurewebsites.net/api/AddLabeledDataClient'}

  $labelingTagsBlobName = Read-Host -Prompt 'Input the name of the josn file that contains all of the valid labeling tags that can be used in labeling training data.. (default=LabelingTags.json)'
  if ([string]::IsNullOrWhiteSpace($labelingTagsBlobName)) {$labelingTagsBlobName = 'LabelingTags.json'}

  while([string]::IsNullOrWhiteSpace($blobSearchServiceName))
  {$blobSearchServiceName = Read-Host -Prompt 'Input the name of the search service that will be used to access the blob binding hash. (default="bindinghashsearch")'
  if ([string]::IsNullOrWhiteSpace($blobSearchServiceName)) {$blobSearchServiceName = 'bindinghashsearch'}
  if ($blobSearchServiceName.length -gt 60){$blobSearchServiceName=$null
    Write-Host "Storage account name cannot be shortern than 2 characters and no longer than 60 charaters." -ForegroundColor "Red"}
  if ($blobSearchServiceName -cmatch '[A-Z]') {$blobSearchServiceName=$null
    Write-Host "Service name must only contain lowercase letters, digits or dashes, cannot use dash as the first two or last one characters, cannot contain consecutive dashes, and is limited between 2 and 60 characters in length." -ForegroundColor "Red"}
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

Write-Host "Creating function app: " $frameworkFunctionAppName -ForegroundColor "Green"

az functionapp create `
  --name $frameworkFunctionAppName `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

$StorageContext = New-AzureStorageContext -ConnectionString $connectionString

$staticStorageContainers = "$pendingEvaluationStorageContainerName $evaluatedDataStorageContainerName $pendingSupervisionStorageContainerName $jsonStorageContainerName" 
Write-Host "Creating static model storage containers: " $staticStorageContainers  -ForegroundColor "Green"
$staticStorageContainers.split() | New-AzStorageContainer -Permission Container -Context $StorageContext

#These storage containers are only used for trained models
if ($modelType -eq "Trained")
{
  $staticStorageContainers = "$pendingNewModelStorageContainerName $modelValidationStorageContainerName $labeledDataStorageContainerName" 
  Write-Host "Creating trained model storage containers: " $staticStorageContainers  -ForegroundColor "Green"
  $staticStorageContainers.split() | New-AzStorageContainer -Permission Container -Context $StorageContext
}

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

  $blobSearchServiceKey = (Invoke-AzureRmResourceAction -Action listAdminKeys -ResourceType "Microsoft.Search/searchServices" -ResourceGroupName 'MLProfessoar' -ResourceName 'bindinghashsearch' -ApiVersion 2015-08-19).primaryKey

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
            "name": "blobInfo",
            "type": "Edm.ComplexType",
            "analyzer": null,
            "synonymMaps": [],
            "fields": [
              {
                "name": "name",
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
                "name": "url",
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
                "name": "modified",
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
              }
            ]
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
        "parameters" : { "maxFailedItems" : "15", "batchSize" : "100", "configuration" : { "parsingMode" : "json", "dataToExtract" : "contentAndMetadata" } }
      }
"@

Invoke-RestMethod -Uri $url -Headers $headers -Method Put -Body $body | ConvertTo-Json

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

Write-Host "Creating app config setting: DataEvaluationServiceEndpoint: " $dataEvaluationServiceEndpoint -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "DataEvaluationServiceEndpoint=$dataEvaluationServiceEndpoint"

Write-Host "Creating app config setting: evaluationDataParameterName: " $evaluationDataParameterName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "evaluationDataParameterName=$evaluationDataParameterName"

#These environment variables are only used for trained models
if ($modelType -eq "Trained")
{

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

Write-Host "Creating app config setting: blobSearchServiceName: " $blobSearchServiceName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchServiceName=$blobSearchServiceName"

Write-Host "Creating app config setting: blobsearchdatasource: " $blobsearchdatasource -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobsearchdatasource=$blobsearchdatasource"

Write-Host "Creating app config setting: blobSearchIndexerName: " $blobSearchIndexerName -ForegroundColor "Green"

az functionapp config appsettings set `
  --name $frameworkFunctionAppName `
  --resource-group $frameworkResourceGroupName `
  --settings "blobSearchIndexerName=$blobSearchIndexerName"
    
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
