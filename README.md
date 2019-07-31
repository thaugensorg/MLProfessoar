
# Semisupervised AI/ML Azure Framework
This solution configures an Azure subscription to enable automated orchestation of semi-supervised AI/ML solutions.  There are two versions that can be deployed, static and trained.  Depending on the model the solution handels all of the invocation of the training and models as well as the management of the contant and its associated labeling.  Data Scientists using this model are required to have minimal knowledge of Azure and the code required to orchestrate a model on Azure.  Models simply have to be invocable via HTTP and respond with JSON.  The interface beyond that is fully configurable such that it often works with existing models with little or no changes to the existing model.  Please see the companion project [Brand Detection](https://github.com/thaugensorg/brandDetection/)

To get started, save the powershell script to your environment will generate an Azure environment for the semisupervised framework.  
Because this framework is dependent on having an analysis model to run, please deploy your analysis model 
before running this script, see the imageAnalysisModel.ps1 script in this directory.  It will save you 
configuration changes after you complete deployment of the framework portion of the solution.  You will 
be prompted for a significant number of parameters about the environment.  As a result, it will help to 
plan your environment in advance.  First, review the architecture image included with the solution as it 
will help you understand the structure of the solution.

# Deploying to Azure
[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)

# Getting started

# Dependencies

# Run it locally

# ...

By participating in this project, you
agree to abide by the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/)
