<# this is a powershell script that will generate an Azure environment for the semisupervised framework.

It does not accept parameters yet, just set the variable values at the top to fit your needs.

This does not yet deploy from source control, that will come in a future version.#>

$storageAccountKey = $null
$resourceGroupName = "semisupervisedFramework"
$storageAccountName = "semisupervisedstorage"
$location = "centralus" #get a list of all the locations and put a link to the web address here.
$subscription = "Thaugen-semisupervised-vision-closed-loop-solution"

if (az group exists --name $resourceGroupName) `
	{az group delete `
	  --name $resourceGroupName `
	  --subscription $subscription `
	  --yes -y}

az group create `
  --name $resourceGroupName `
  --location $location

az storage account create `
    --location centralus `
    --name $storageAccountName `
    --resource-group $resourceGroupName `
    --sku Standard_LRS

$storageAccountKey = `
	(get-azureRmStorageAccountKey `
		-resourceGroupName $resourceGroupName `
		-AccountName $storageAccountName).Value[0]

az functionapp create `
  --name semisupervisedApp `
  --storage-account $storageAccountName `
  --consumption-plan-location centralus `
  --resource-group $resourceGroupName

az storage container create `
  --name labeledtrainingdata `
  --account-name $storageAccountName `
  --account-key $storageAccountKey

az storage container create `
  --name pendingsupervision `
  --account-name $storageAccountName `
  --account-key $storageAccountKey `
  --fail-on-exist

az storage container create `
  --name pendingevaluation `
  --account-name $storageAccountName `
  --account-key $storageAccountKey `
  --fail-on-exist

az storage container create `
  --name evaluateddata `
  --account-name $storageAccountName `
  --account-key $storageAccountKey `
  --fail-on-exist

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

