$title = "Confirm Deletion?"
$message = "Running test script will delete all blobs from storage.  Do you want to run test script?"
$No = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "No"
$Yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Yes"
$options = [System.Management.Automation.Host.ChoiceDescription[]]($No, $Yes)
$Confirmed=$host.ui.PromptForChoice($title, $message, $options, 0)

if ($Confirmed == 0) {break}

Delete files from containers

copy supervised file to pendingevaluation container
Validate file with same name is in pending supervision container
validate JSON file with matching GUID is in evaluatedJson container
Log results

copy high confidence file to pending evaluation container
validate file with same name is in evaluateddata container
validate JSON file with matching GUID is in evaluatedJson Container 
log results