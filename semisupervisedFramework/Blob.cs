﻿using System;
using System.Text;
using System.Buffers;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

using Newtonsoft.Json.Linq;


namespace semisupervisedFramework
{
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    // This class is provided to assist newtonsoft in deserializing json blob search results into objects
    // that can be reasoned over.  *****TODO***** it is unclear if this is still required as a JObject
    // class may be just as effective given this class has no behavior.
    //**********************************************************************************************************
    public class BlobInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }

        [IsSearchable]
        public string Md5Hash { get; set; }
        public DateTimeOffset StateChange { get; set; }

        public override string ToString()
        {
            return $"Name: {Name}\tURL: {Url}\tHash: {Md5Hash}\tStateChanged: {StateChange}";
        }
    }
    
    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    //This class provides the common functionality for DataBlob and JsonBlob subclasses.  This shared behavior 
    //is generally dealing with hashes and the ability to construct and navigate between a pair of bound DataBlob
    //and JsonBlobs.  THe binsing of these two files using MD5 hases is one of the key values of ML Proferssoar.
    //It allows the orchestration engine to handle any type of data that needs a supervision loop as some data
    //file types do not have the ability to embed an arbitrary amount of json data.
    //**********************************************************************************************************

    public abstract class FrameworkBlob
    {
        // we have to use a 'has a' relationship here as oposed to an 'is a' relationship because using CloudBlockBlob as a base class requires
        // the constructor to pass a URI and the primary behavior of the blob class is navigating between data and json blob types
        // using the hash value to *retrieve* the URL so we do not have the URL when instanciating so cannot instanciate CloudBlockBlob.
        public CloudBlockBlob AzureBlob { get; set; }
        virtual public Search Search { get; set; }
        virtual public Engine Engine { get; set; }

        // encapsulates the GetBlobByHash behavior which is reused between both DataBlob and JsonBlob subclasses
        public FrameworkBlob(Engine engine, Search search)
        {
            Engine = engine;
            Search = search;
        }

        // calculates a blob hash to join JSON to a specific version of a file.
        private async Task<string> CalculateBlobHash(CloudBlockBlob blockBlob, ILogger _Log)
        {
            try
            {
                MemoryStream memStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(memStream);
                if (memStream.Length == 0)
                {
                    throw (new ZeroLengthFileException($"\nCloud Block Blob: {blockBlob.Name} is zero length"));
                }
                string blobString = memStream.ToString();
                string md5Hash = CalculateMD5Hash();

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
                _Log.LogInformation($"\n{blockBlob.Name} is zero length.  CalculateBlobFileHash failed with error: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                _Log.LogInformation($"\ncalculatingBlobFileHash for {blockBlob.Name} failed with: {e.Message}");
                return null;
            }
        }

        public string CalculateMD5Hash()
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(AzureBlob.ToString());
            byte[] md5Hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < md5Hash.Length; i++)
            {
                sb.Append(md5Hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public DocumentSearchResult<JObject> GetBlobByHash(SearchIndexClient indexClient, string md5Hash)
        {
            SearchParameters parameters;

            parameters =
                new SearchParameters()
                {
                    //SearchFields = new[] { "Hash" },
                    Select = new[] { "Id", "Name", "Hash" , "metadata_storage_path" }
                };

            //return indexClient.Documents.Search<BlobInfo>(Hash, parameters);
            DocumentSearchResult<JObject> result = indexClient.Documents.Search<JObject>(md5Hash);
            return result;

        }

        public async Task<Uri> GetDataBlobUriFromJson(string md5Hash)
        {
            SearchIndexClient indexClient = await GetJsonBindingSearchIndex();
            JObject searchResult = JsonBindingSearchResult(indexClient, md5Hash);
            JToken urlToken = searchResult.SelectToken("url");
            if (urlToken != null)
            {
                return new Uri(urlToken.ToString());
            }
            else
            {
                throw (new MissingRequiredObject($"\nBound JSON for {md5Hash} does not contain a url value."));
            }
        }

        public async Task<JObject> SearchForBoundJson(string md5Hash)
        {
            try
            {
                SearchIndexClient indexClient = await GetJsonBindingSearchIndex();
                return JsonBindingSearchResult(indexClient, md5Hash);
            }
            catch
            {
                throw;
            }
        }

        public async Task<SearchIndexClient> GetJsonBindingSearchIndex()
        {
            string blobSearchIndexName = Engine.GetEnvironmentVariable("blobSearchIndexName");
            return await Search.CreateSearchIndexClient(blobSearchIndexName);
        }

        public JObject JsonBindingSearchResult(SearchIndexClient indexClient, string md5Hash)
        {
            DocumentSearchResult<JObject> documentSearchResult = GetBlobByHash(indexClient, md5Hash);
            if (documentSearchResult.Results.Count > 0)
            {
                JObject firstSearchResult = documentSearchResult.Results[0].Document;
                return firstSearchResult;
            }
            throw (new MissingRequiredObject("\nNo search results returned from index using {md5Hash}."));
        }
    }

    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    //This class encapsulates the fucntionality for the data blob files that will be used to train the semisupervised model.
    //**********************************************************************************************************
    public class DataBlob : FrameworkBlob
    {
        //public Log;
        private JsonBlob _jsonBlob;
        public JsonBlob BoundJsonBlob
        {
            get
            {
                if (_jsonBlob == null)
                {
                    _jsonBlob = new JsonBlob(AzureBlob.Properties.ContentMD5, Engine, Search);
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

        public DataBlob(string md5Hash, Engine engine, Search search) : base(engine, search)
        {
            Uri DataBlobUri = GetDataBlobUriFromJson(md5Hash).Result;
            CloudStorageAccount StorageAccount = Engine.StorageAccount;
            CloudBlobClient BlobClient = StorageAccount.CreateCloudBlobClient();
            AzureBlob = new CloudBlockBlob(DataBlobUri, BlobClient);
        }

        public DataBlob(CloudBlockBlob azureBlob, Engine engine, Search search, ILogger log) : base(engine, search)
        {
            AzureBlob = azureBlob;
        }

        private async Task<string> CalculateMD5Async()
        {
            //*****TODO***** it is not clear if the standard api for calculating the MD5 hash in Azure pages the hash calculation or if I have to do it manually to provide performance when working with very large data files.
            // https://stackoverflow.com/questions/2124468/possible-to-calculate-md5-or-other-hash-with-buffered-reads
            // http://www.infinitec.de/post/2007/06/09/Displaying-progress-updates-when-hashing-large-files.aspx
            // https://stackoverflow.com/questions/24312527/azure-blob-storage-downloadtobytearray-vs-downloadtostream
            // https://stackoverflow.com/questions/6752000/downloading-azure-blob-files-in-mvc3
            byte[] block = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    MemoryStream memStream = new MemoryStream();
                    using (var stream = new FileStream(AzureBlob.Name, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
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
    }

    //**********************************************************************************************************
    //                      CLASS DESCRIPTION
    //This class encapsulates the functionality for the json blob files that are bound to the data files.  These
    //json files contain all of the meta data, labeling data, and results of every evaluation against the model.
    //**********************************************************************************************************
    public class JsonBlob : FrameworkBlob
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }
        public string Name { get; set; }
        public JObject Json { get; set; }
        public string Md5Hash { get; set; }
        public string Labels { get; set; }
        private DataBlob _DataBlob;

        public DataBlob DataBlob
        {
            get
            {
                if (_DataBlob == null)
                {
                    DataBlob dataBlob = new DataBlob(Md5Hash, Engine, Search);
                    _DataBlob = dataBlob ?? throw new MissingRequiredObject($"\nNo data blob found with MD5 hash {Md5Hash}.");
                }
                return _DataBlob;
            }
            set => DataBlob = value;
        }

        public JsonBlob(string md5Hash, Engine engine, Search search) : base(engine, search)
        {

            // Get a reference to the json blob and hydrate it into the json blob class attributes.
            //*****TODO***** there is a way to hydrate directly from a JObject to a C# object in one line, update this logic to simplify the code.
            CloudStorageAccount storageAccount = Engine.StorageAccount;
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer jsonContainer = blobClient.GetContainerReference(Engine.GetEnvironmentVariable("jsonStorageContainerName"));
            string boundJsonFileName = engine.GetEncodedHashFileName(md5Hash);
            AzureBlob = jsonContainer.GetBlockBlobReference(boundJsonFileName);

            if (!AzureBlob.Exists())
            {
                engine.Log.LogInformation($"\nSearch did not find a Json blob {boundJsonFileName} using MD5 hash: {md5Hash} processing cannot continue.");
                throw (new MissingRequiredObject($"\nBound JSON for {md5Hash} does not exist."));
            }

            // load the json blob into a JObject
            Json = JObject.Parse(AzureBlob.DownloadText());

            JToken idToken = Json.SelectToken("Id");
            if (idToken != null)
            {
                Id = idToken.ToString();
            }
            else
            {
                throw (new MissingRequiredObject($"\nBound JSON for {md5Hash} does not contain an id value."));
            }

            JToken labelsToken = Json.SelectToken("labels");
            if (labelsToken != null)
            {
                Labels = labelsToken.ToString();
            }
            // No exeption is thrown when labels value is not found as it simply means the Json file has not yet been populated with
            // the labels Json value.  THis normally happens when a new file is added to the system.  The file has a bound Json file
            // created but the file has not yet been labeled yet so there is no label label in the Json.  As soon as the file is labeled
            // the label label will be populated.

            JToken md5HashToken = Json.SelectToken("Hash");
            if (md5HashToken != null)
            {
                Md5Hash = md5HashToken.ToString();
            }
            else
            {
                throw (new MissingRequiredObject($"\nBound JSON for {md5Hash} does not contain an Hash value."));
            }

            JToken nameToken = Json.SelectToken("Name");
            if (nameToken != null)
            {
                Name = nameToken.ToString();
            }
            else
            {
                throw (new MissingRequiredObject($"\nBound JSON for {md5Hash} does not contain an Name value."));
            }
        }
    }
}
