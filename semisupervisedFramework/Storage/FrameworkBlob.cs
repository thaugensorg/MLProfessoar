using System;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json.Linq;
using semisupervisedFramework.Exceptions;

namespace semisupervisedFramework.Storage
{
    public class FrameworkBlob
    {
        //we have to use has a relationship here as oposed to is a because using CloudBlockBlob as a base class requires
        //the constructor to pass a URI and the primary behavior of the blob class is navigating between data and json blob types
        //using the hash value to retrieve the URL.
        public CloudBlockBlob AzureBlob { get; set; }
        public ILogger Log { get; set; }

        // encapsulates the GetBlobByHash behavior which is reused between both DataBlob and JsonBlob subclasses.
        public FrameworkBlob() { }

        //calculates a blob hash to join JSON to a specific version of a file.
        private async Task<string> CalculateBlobHash(CloudBlockBlob blockBlob, ILogger log)
        {
            try
            {
                var MemStream = new MemoryStream();
                await blockBlob.DownloadToStreamAsync(MemStream);
                if (MemStream.Length == 0)
                {
                    throw new ZeroLengthFileException("\nCloud Block Blob: " + blockBlob.Name + " is zero length");
                }
                var BlobString = MemStream.ToString();
                var md5Hash = CalculateMD5Hash(BlobString);

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
            var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var md5Hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            var sb = new StringBuilder();
            for (var i = 0; i < md5Hash.Length; i++)
            {
                sb.Append(md5Hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static DocumentSearchResult<JObject> GetBlobByHash(SearchIndexClient indexClient, string md5Hash, ILogger log)
        {
            SearchParameters parameters;

            parameters =
                new SearchParameters()
                {
                    //SearchFields = new[] { "hash" },
                    Select = new[] { "id", "blobInfo/name", "blobInfo/url", "blobInfo/hash", "blobInfo/modified" }
                };

            //return indexClient.Documents.Search<BlobInfo>(hash, parameters);
            var result = indexClient.Documents.Search<JObject>(md5Hash);
            return result;

        }

        public Uri GetDataBlobUriFromJson(string md5Hash)
        {
            var indexClient = GetJsonBindingSearchIndex();
            var searchResult = JsonBindingSearchResult(indexClient, md5Hash);
            var urlToken = searchResult.SelectToken("blobInfo.url");
            if (urlToken != null)
            {
                return new Uri(urlToken.ToString());
            }
            else
            {
                throw new MissingRequiredObjectException($"\nBound JSON for {md5Hash} does not contain a blobInfo.url name.");
            }
        }

        public JObject GetJsonBlobJson(string md5Hash)
        {
            var indexClient = GetJsonBindingSearchIndex();
            return JsonBindingSearchResult(indexClient, md5Hash);
        }

        public SearchIndexClient GetJsonBindingSearchIndex()
        {
            var BindingSearch = new Search();
            return Search.CreateSearchIndexClient("data-labels-index", Log);
        }

        public JObject JsonBindingSearchResult(SearchIndexClient indexClient, string md5Hash)
        {
            var documentSearchResult = GetBlobByHash(indexClient, md5Hash, Log);
            if (documentSearchResult.Results.Count > 0)
            {
                var firstSearchResult = documentSearchResult.Results[0].Document;
                return firstSearchResult;
            }
            throw new MissingRequiredObjectException("\nNo search results returned from index using {md5Hash}.");
        }
    }
}
