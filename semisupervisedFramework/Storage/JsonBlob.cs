using System;
using System.Collections.Generic;
using System.Buffers;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Newtonsoft.Json;

namespace semisupervisedFramework.Storage
{
    //This class encapsulates the functionality for the json blob files that are bound to the data files.  These json files contain all of the meta data, labeling data, and results of every evaluation against the model
    public class JsonBlob : FrameworkBlob
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }
        //private BlobInfo _blobInfo;
        public BlobInfo BlobInfo;
        public IList<string> Labels { get; set; }
        private DataBlob _DataBlob;
        public DataBlob BoundDataBlob
        {
            get
            {
                if (_DataBlob == null)
                {
                    _DataBlob = new DataBlob(BlobInfo.Md5Hash, Log);
                    return _DataBlob;
                }
                else
                {
                    return _DataBlob;
                }
            }
            set => BoundDataBlob = value;
        }

        public JsonBlob(string md5Hash, ILogger log)
        {
            BlobInfo = new BlobInfo();
            Log = log;
            var jsonBlobJson = GetJsonBlobJson(md5Hash);
            //JsonBlob BoundJson = dataBlob.GetBoundJson(log);
            //*****TODO***** get the url to the actual blob and down load the JOSON full JSON content not just what was indexed
            var idToken = jsonBlobJson.SelectToken("id");
            if (idToken != null)
            {
                Id = idToken.ToString();
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an id name.");
            }

            var labelsToken = jsonBlobJson.SelectToken("labels");
            if (idToken != null)
            {
                var labelsJson = labelsToken.ToString();
                Labels = JsonConvert.DeserializeObject<IList<string>>(labelsJson);
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an labels name.");
            }

            var md5HashToken = jsonBlobJson.SelectToken("blobInfo.hash");
            if (idToken != null)
            {
                BlobInfo.Md5Hash = md5HashToken.ToString();
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an blobInfo.hash name.");
            }

            var modifiedToken = jsonBlobJson.SelectToken("blobInfo.modified");
            if (idToken != null)
            {
                var dateTimeFormat = "MM'/'dd'/'yyyy hh':'mm':'ss tt zzzz";
                var dateJsonToken = modifiedToken.ToString();
                try
                {
                    //*****TODO***** update the format string to come from an environment variable that forces this date time format to align with the date time format used to create the value when the json is built.
                    //*****TODO***** add culture handling using IFormatProvider so that this adjusts date time format in a single configuration location.
                    BlobInfo.Modified = DateTime.Parse(dateJsonToken, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    throw new FormatException($"\nThe following date: {dateJsonToken} does not match the format: {dateTimeFormat}.");
                }
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an blobInfo.modified name.");
            }

            var nameToken = jsonBlobJson.SelectToken("blobInfo.name");
            if (idToken != null)
            {
                BlobInfo.Name = nameToken.ToString();
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an blobInfo.name name.");
            }

            var urlToken = jsonBlobJson.SelectToken("blobInfo.url");
            if (idToken != null)
            {
                BlobInfo.Url = urlToken.ToString();
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain an blobInfo.url name.");
            }
        }

        private static async Task<string> CalculateMD5Async(CloudBlockBlob blockBlob, ILogger log)
        {
            //*****TODO***** it is not clear if the standard api for calculating the MD5 hash in Azure pages the hash calculation or if I have to do it manually.
            // https://stackoverflow.com/questions/2124468/possible-to-calculate-md5-or-other-hash-with-buffered-reads
            // http://www.infinitec.de/post/2007/06/09/Displaying-progress-updates-when-hashing-large-files.aspx
            // https://stackoverflow.com/questions/24312527/azure-blob-storage-downloadtobytearray-vs-downloadtostream
            // https://stackoverflow.com/questions/6752000/downloading-azure-blob-files-in-mvc3
            var block = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using (var md5 = MD5.Create())
                {
                    var MemStream = new MemoryStream();
                    using (var stream = new FileStream(blockBlob.Name, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
                    {
                        int length;
                        while ((length = await stream.ReadAsync(block, 0, block.Length).ConfigureAwait(false)) > 0)
                        {
                            md5.TransformBlock(block, 0, length, null, 0);
                        }
                        md5.TransformFinalBlock(block, 0, 0);
                    }
                    var md5Hash = md5.Hash;
                    return Convert.ToBase64String(md5Hash);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }

        public DataBlob GetBoundData(CloudBlobContainer Container)
        {
            //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
            //*****TODO***** This uses the file name as the searcdh mechanism.  I expect if the file name changes so does the hash but this has not been verified.  If the name does not change the hash then I need to locate the file using the has which will mean creating a search index over the blob file properties.
            var RawDataBlob = Container.GetBlockBlobReference(BlobInfo.Name);
            var TrainingDataBlob = new DataBlob(BlobInfo.Md5Hash, Log);
            if (TrainingDataBlob == null)
            {
                throw new MissingRequiredObjectException("\nMissing dataEvaluating blob object.");
            }

            return TrainingDataBlob;

        }
    }
}
