using System;
using System.Collections.Generic;
using System.Text;
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
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace semisupervisedFramework
{
    public partial class BlobInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }

        [IsSearchable]
        public string Hash { get; set; }
        public DateTimeOffset Modified { get; set; }

        public override string ToString()
        {
            return $"Name: {Name}\tURL: {Url}\tHash: {Hash}\tModified: {Modified}";
        }
    }

    public class FrameworkBlob : CloudBlockBlob
    {
        // encapsulates the GetBlobByHash behavior which is reused between both DataBlob and JsonBlob subclasses.
        public FrameworkBlob(Uri blobUri) : base(blobUri) { }

        //calculates a blob hash to join JSON to a specific version of a file.
        private async Task<string> CalculateBlobHash(CloudBlockBlob blockBlob, ILogger log)
        {
            try
            {
                MemoryStream MemStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(MemStream);
                if (MemStream.Length == 0)
                {
                    throw (new ZeroLengthFileException("\nCloud Block Blob: " + blockBlob.Name + " is zero length"));
                }
                string BlobString = MemStream.ToString();
                string md5Hash = CalculateMD5Hash(BlobString);

                // ***** TODO ***** check if chunking the file download is necissary or if the azure blob movement namepace handles chunking for you.
                // Download will re-populate the client MD5 value from the server
                //byte[] retrievedBuffer = blockBlob.DownloadToByteArrayAsync();

                // Validate MD5 Value
                //var md5Check = System.Security.Cryptography.MD5.Create();
                //md5Check.TransformBlock(retrievedBuffer, 0, retrievedBuffer.Length, null, 0);
                //md5Check.TransformFinalBlock(new byte[0], 0, 0);

                // Get Hash Value
                //byte[] hashBytes = md5Check.Hash;
                //string hashVal = Convert.ToBase64String(hashBytes);

                return md5Hash;
            }
            catch (ZeroLengthFileException e)
            {
                log.LogInformation("\n" + blockBlob.Name + " is zero length.  CalculateBlobFileHash failed with error: " + e.Message);
                return null;
            }
            catch (Exception e)
            {
                log.LogInformation("\ncalculatingBlobFileHash for " + blockBlob.Name + " failed with: " + e.Message);
                return null;
            }
        }
        public static string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static DocumentSearchResult<JsonBlob> GetBlobByHash(SearchIndexClient indexClient, string hash, ILogger log)
        {
            SearchParameters parameters;

            parameters =
                new SearchParameters()
                {
                    //SearchFields = new[] { "hash" },
                    Select = new[] { "id", "blobInfo/name", "blobInfo/url", "blobInfo/hash", "blobInfo/modified" }
                };

            //return indexClient.Documents.Search<BlobInfo>(hash, parameters);
            DocumentSearchResult<JsonBlob> result = indexClient.Documents.Search<JsonBlob>(hash);
            return result;

        }
    }

    //This class encapsulates the fucntionality for the data blob files that will be used to train the semisupervised model.
    public class DataBlob : FrameworkBlob
    {
        public DataBlob(Uri dataBlobUri) : base(dataBlobUri){ }

        public DataBlob(Uri dataBlobUri, CloudBlockBlob dataBlob) : base(dataBlobUri)
        {
            BaseClass bc = JsonConvert.DeserializeObject<BaseClass>(JsonConvert.SerializeObject(B));
            this.BlobType = dataBlob.BlobType;
            this.Container = dataBlob.Container;
            this.CopyState = dataBlob.CopyState;
            this.IsDeleted = dataBlob.IsDeleted;
            this.IsSnapshot = dataBlob.IsSnapshot;
            this.Metadata = dataBlob.Metadata;
            this.Name = dataBlob.Name;
            this.Properties = dataBlob.Properties;
            this.ServiceClient = dataBlob.ServiceClient;
            this.StorageUri = dataBlob.StorageUri;

        }

        //*****TODO***** I believe this should be deprecated but saving the functionality until positive.
        private JsonBlob GetBoundJson(ILogger log)
        {
            Search BindingSearch = new Search();
            SearchIndexClient IndexClient = Search.CreateSearchIndexClient("data-labels-index", log);
            DocumentSearchResult<JsonBlob> documentSearchResult = GetBlobByHash(IndexClient, this.Properties.ContentMD5, log);
            if (documentSearchResult.Results.Count > 0)
            {
                return documentSearchResult.Results[0].Document;
            }
            return null;
        }
    }

    //This class encapsulates the functionality for the json blob files that are bound to the data files.  These json files contain all of the meta data, labeling data, and results of every evaluation against the model
    public class JsonBlob : FrameworkBlob
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }
        public BlobInfo blobInfo;
        public IList<string> Labels { get; set; }

        public JsonBlob(Uri jsonBlobUri, string md5Hash, ILogger log) : base(jsonBlobUri)
        {
            Search BindingSearch = new Search();
            SearchIndexClient IndexClient = Search.CreateSearchIndexClient("data-labels-index", log);
            DocumentSearchResult<JsonBlob> documentSearchResult = GetBlobByHash(IndexClient, this.Properties.ContentMD5, log);
            if (documentSearchResult.Results.Count > 0)
            {
                //JsonBlob BoundJson = dataBlob.GetBoundJson(log);
                //*****TODO***** get the url to the actual blob and down load the JOSON full JSON content not just what was indexed
                string DataTrainingLabels = JsonConvert.SerializeObject(documentSearchResult);
                JObject LabelsJsonObject = JObject.Parse(DataTrainingLabels);
                JToken LabelsToken = LabelsJsonObject.SelectToken("labels");
                string labels = Uri.EscapeDataString(LabelsToken.ToString());

                JToken idToken = LabelsJsonObject.SelectToken("id");
                Id = idToken.ToString();

                JToken labelsToken = LabelsJsonObject.SelectToken("labels");
                string labelsJson = labelsToken.ToString();
                Labels = JsonConvert.DeserializeObject<IList<string>>(labelsJson);

                JToken hashToken = LabelsJsonObject.SelectToken("blobInfo/hash");
                blobInfo.Hash = hashToken.ToString();

                JToken modifiedToken = LabelsJsonObject.SelectToken("blobInfo/modified");
                blobInfo.Modified = DateTime.ParseExact(modifiedToken.ToString(), "MMM dd YYYY H:mmtt", CultureInfo.InvariantCulture);

                JToken nameToken = LabelsJsonObject.SelectToken("blobInfo/name");
                blobInfo.Name = nameToken.ToString();

                JToken urlToken = LabelsJsonObject.SelectToken("blobInfo/url");
                blobInfo.Url = urlToken.ToString();
            }
        }

        private static async Task<string> CalculateMD5Async(CloudBlockBlob blockBlob, ILogger log)
        {
            //*****TODO***** it is not clear if the standard api for calculating the MD5 hash in Azure pages the hash calculation or if I have to do it manually.
            // https://stackoverflow.com/questions/2124468/possible-to-calculate-md5-or-other-hash-with-buffered-reads
            // http://www.infinitec.de/post/2007/06/09/Displaying-progress-updates-when-hashing-large-files.aspx
            // https://stackoverflow.com/questions/24312527/azure-blob-storage-downloadtobytearray-vs-downloadtostream
            // https://stackoverflow.com/questions/6752000/downloading-azure-blob-files-in-mvc3
            byte[] block = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    MemoryStream MemStream = new MemoryStream();
                    using (var stream = new FileStream(blockBlob.Name, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
                    {
                        int length;
                        while ((length = await stream.ReadAsync(block, 0, block.Length).ConfigureAwait(false)) > 0)
                        {
                            md5.TransformBlock(block, 0, length, null, 0);
                        }
                        md5.TransformFinalBlock(block, 0, 0);
                    }
                    var hash = md5.Hash;
                    return Convert.ToBase64String(hash);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }

        public DataBlob GetBoundData(CloudBlobContainer Container, ILogger log)
        {
            //Get a reference to a container, if the container does not exist create one then get the reference to the blob you want to evaluate."
            //*****TODO***** This uses the file name as the searcdh mechanism.  I expect if the file name changes so does the hash but this has not been verified.  If the name does not change the hash then I need to locate the file using the has which will mean creating a search index over the blob file properties.
            CloudBlockBlob RawDataBlob = Container.GetBlockBlobReference(this.blobInfo.Name);
            DataBlob TrainingDataBlob = new DataBlob(RawDataBlob.Uri);
            if (TrainingDataBlob == null)
            {
                throw (new MissingRequiredObject("\nMissing dataEvaluating blob object."));
            }

            return TrainingDataBlob;

        }
    }
}
