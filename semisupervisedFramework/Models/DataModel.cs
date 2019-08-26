
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage.Blob;

namespace semisupervisedFramework.Models
{
    //This class encapsulates the fucntionality for the data blob files that will be used to train the semisupervised model.
    public class DataModel : BaseModel
    {
        private JsonModel _jsonBlob;
        public JsonModel BoundJsonBlob
        {
            get
            {
                if (_jsonBlob == null)
                {
                    _jsonBlob = new JsonModel(AzureBlob.Properties.ContentMD5, Log);
                    return _jsonBlob;
                }
                else
                {
                    return _jsonBlob;
                }
                //add json blob object logic if json blob is null
            }
            set => BoundJsonBlob = value;
        }

        public DataModel(string md5Hash, ILogger log)
        {
            Log = log;
            var dataBlobUri = GetDataBlobUriFromJson(md5Hash);
            var storageAccount = Engine.GetStorageAccount(log);
            var blobClient = storageAccount.CreateCloudBlobClient();
            AzureBlob = new CloudBlockBlob(dataBlobUri, blobClient);
        }

        public DataModel(CloudBlockBlob azureBlob, ILogger log)
        {
            Log = log;
            AzureBlob = azureBlob;
        }
    }
}
