$command = '.\MLProfessoarEnvironmentConfiguration.ps1 `
    -subscription Thaugen-semisupervised-vision-closed-loop-solution `
    -frameworkResourceGroupName MLProfessoar `
    -frameworkLocation westus `
    -modelType Trained `
    -evaluationDataParameterName dataBlobUrl `
    -labelsJsonPath labels.regions[0].tags `
    -confidenceJSONPath confidence `
    -dataEvaluationServiceEndpoint https://mlpobjectdetectionapp.azurewebsites.net/api/EvaluateData `
    -confidenceThreshold .95 `
    -modelVerificationPercentage .05 `
    -trainModelServiceEndpoint https://mlpobjectdetectionapp.azurewebsites.net/api/TrainModel `
    -tagsUploadServiceEndpoint https://mlpobjectdetectionapp.azurewebsites.net/api/LoadLabelingTags `
    -LabeledDataServiceEndpoint https://mlpobjectdetectionapp.azurewebsites.net/api/AddLabeledData `
    -LabelingSolutionName VoTT `
    -labelingTagsParameterName labelsJson'
Invoke-Expression $command