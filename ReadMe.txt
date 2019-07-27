This powershell script will generate an Azure environment for the semisupervised framework.  
Because this framework is dependent on having an analysis model to run, please deploy your analysis model 
before running this script, see the imageAnalysisModel.ps1 script in this directory.  It will save you 
configuration changes after you complete deployment of the framework portion of the solution.  You will 
be prompted for a significant number of parameters about the environment.  As a result, it will help to 
plan your environment in advance.  First, review the architecture image included with the solution as it 
will help you understand the structure of the solution.

Then collect all of these values:
- the name of the subscription where this solution will be deployed
- the name of the resource group that you want to create for installing this orchestration framework for 
managing semisupervised models (default = semisupervisedFramework)
- the name of the azure storage account you want to create for this installation of the orchestration 
framework (default = semisupervisedstorage)
- the name for the azure function app you want to create for this installation of the orchestration 
framework (default = semisupervisedApp)
- the Azure location, data center, where you want this solution deployed.  Note, if you will be using 
Python functions as part of your solution you must carefully choose your azure location, As of 8/1/19, 
Python functions are only available in eastasia, eastus, northcentralus, northeurope, westeurope, and 
westus.  If you deploy your solution in a different data center network transit time may affect your 
solution performance (default = westus)
- the model type you want to deploy, static, meaning the framework does not support a training loop or 
trained which supports a full training loop.
- the name of the storage container for blobs to be evaluated by the model configured in this framework 
(default = pendingevaluation)
- the name of the storage container for blobs after they are evaluated by the model (default = evaluateddata)
- the name of the storage container for JSON blobs containing data generated from the blobs evaluated by 
this model (default = evaluatedjson)
- the name of the storage container for blobs that require supervision after they have been evaluated by 
the model (default = pendingsupervision)
- the JSON path where the blob analysis confidence value will be found in the JSON document found in the 
model analysis response.  By default, "confidence" is expected as a root key in the response JSON 
(default = confidence)
- the decimal value in the format of a C# Double that specifies the confidence threshold the model must return 
to indicate the model blob analysis is acceptable (default = .95)

if you choose to deploy a trained model then you will need to have the following values also available:
- the name of the storage container for blobs that will store labeled data for training the model (default=labeleddata)
- the name of the storage container for blobs that will be used to validate the model after they have been
evaluated by the model (default=modelvalidation)
- the name of the storage container for blobs that need to be re-evaluated after a new mode has been
published (default=pendingnewmodelevaluation)
- the decimal value in the format of a C# Double that specifies the percentage of successfully evaluated 
blobs to be routed to a verification queue (default=.05)

This solution does not yet deploy from source control, that will come in a future version.

If you just want to run the packaged static azure vision service model then you are done once you complete
the deployment.  Simply upload and run both of the powershell scripts, .ps1 files, to your azure
subscription.  This article shows how to upload and run powershell scripts in Azure:
https://www.ntweekly.com/2019/05/24/upload-and-run-powershell-script-from-azure-cloud-shell/
