<# this is a powershell script that will generate an Azure environment for the semisupervised framework.

It does not accept parameters yet, just set the variable values at the top to fit your needs.

This does not yet deploy from source control, that will come in a future version.#>

$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

#variables for framework
$frameworkResourceGroupName = "semisupervisedFramework"
$frameworkStorageAccountName = "semisupervisedstorage"
$frameworkStorageAccountKey = $null
$frameworkLocation = "centralus" #get a list of all the locations and put a link to the web address here.

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
  --name semisupervisedApp `
  --storage-account $frameworkStorageAccountName `
  --consumption-plan-location $frameworkLocation `
  --resource-group $frameworkResourceGroupName

az storage container create `
  --name labeledtrainingdata `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey 

az storage container create `
  --name pendingsupervision `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name pendingevaluation `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

az storage container create `
  --name evaluateddata `
  --account-name $frameworkStorageAccountName `
  --account-key $frameworkStorageAccountKey `
  --fail-on-exist

$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

$modelResourceGroupName = "imageAnalysisModel"
$modelStorageAccountName = "imageanalysisstorage"
$modelStorageAccountKey = $null
$modelLocation = "westus"

if (az group exists --name $modelResourceGroupName) `
	{az group delete `
	  --name $modelResourceGroupName `
	  --subscription $subscription `
	  --yes -y}

az group create `
  --name $modelResourceGroupName `
  --location $modelLocation 

az storage account create `
    --location $modelLocation `
    --name $modelStorageAccountName `
    --resource-group $modelResourceGroupName `
    --sku Standard_LRS

$modelStorageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $modelResourceGroupName `
		-AccountName $modelStorageAccountName).Value[0]

az functionapp create `
  --name brandDetectionApp `
  --storage-account $modelStorageAccountName `
  --consumption-plan-location $modelLocation `
  --resource-group $modelResourceGroupName `
  --os-type "Linux" `
  --runtime "python"

az cognitiveservices account create `
    --name "brandDetection" `
    --resource-group $modelResourceGroupName `
    --kind ComputerVision `
    --sku F0 `
    --location westus `
    --yes

az cognitiveservices account create `
    --name "brandDetection" `
    --resource-group $modelResourceGroupName `
    --kind ComputerVision `
    --sku F0 `
    --location westus `
    --yes

#gitrepo=https://github.com/thaugensorg/semi-supervisedModelSolution.git
#token=<Replace with a GitHub access token>

# Enable authenticated git deployment in your subscription from a private repo.
#az functionapp deployment source update-token \
#  --git-token $token

# Create a function app with source files deployed from the specified GitHub repo.
#az functionapp create \
#  --name autoTestDeployment \
#  --storage-account semisupervisedstorage \
#  --consumption-plan-location centralUS\
#  --resource-group customVisionModelTest \
#  --deployment-source-url https://github.com/thaugensorg/semi-supervisedModelSolution.git \
#  --deployment-source-branch master

