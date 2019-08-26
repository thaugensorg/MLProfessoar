
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage.Blob;

namespace semisupervisedFramework.Storage
{
    //This class encapsulates the fucntionality for the data blob files that will be used to train the semisupervised model.
    public class DataBlob : FrameworkBlob
    {
        private JsonBlob _jsonBlob;
        public JsonBlob BoundJsonBlob
        {
            get
            {
                if (_jsonBlob == null)
                {
                    _jsonBlob = new JsonBlob(AzureBlob.Properties.ContentMD5, Log);
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

        public DataBlob(string md5Hash, ILogger log)
        {
            Log = log;
            var dataBlobUri = GetDataBlobUriFromJson(md5Hash);
            var storageAccount = Engine.GetStorageAccount(log);
            var blobClient = storageAccount.CreateCloudBlobClient();
            AzureBlob = new CloudBlockBlob(dataBlobUri, blobClient);
        }

        public DataBlob(CloudBlockBlob azureBlob, ILogger log)
        {
            Log = log;
            AzureBlob = azureBlob;
        }
    }
}
