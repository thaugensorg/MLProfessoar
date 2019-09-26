using System;

using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class is the timer that launches the model training process using labeled data from the labeleddata
    // Azure blob container.
    //**********************************************************************************************************

    public static class TrainingTimer
    {
        //*****TODO***** Externalize timer frequency.
        [FunctionName("TrainingTimer")]
        public async static void Run(
                [TimerTrigger("0 */60 * * * *" //setting this to 1 will cause the trigger to fire every minute for debug purposes.

            //This setting causes the timer job to immediately run when you press F5 rather than having to wait for the timer to fire after n minutes.  Set the line below to true if you want to debug the timer process.
#if DEBUG
            , RunOnStartup = false
#endif            

            )]TimerInfo myTimer, ILogger log)
        {

            Engine engine = new Engine(log);

            //if the model is not type trained then skip the training loop.
            string modelType = engine.GetEnvironmentVariable("modelType", log);
            log.LogInformation($"\nBranck condition set to foo blocking training proces from running while developing.  Must be set to Trained for release build.");

            if (modelType == "foo")  // make this foo to stop the process from running while in development "Trained")
            {
                Search search = new Search(engine, log);
                Model model = new Model(engine, search, log);
                await model.TrainingProcess();
            }
            else
            {
                log.LogInformation($"\nModel set to 'Static' execiution of training logic suspended.  If you would like to change this to a trained model please update appropriate environment variables.  If you were waiting for the pending evaluation blob trigger to fire and it is not firing then you probably do not have the right storage key set in your local.setting.json file.");
            }
        }
    }
}
