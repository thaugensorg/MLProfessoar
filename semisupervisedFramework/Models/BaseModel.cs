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

namespace semisupervisedFramework.Models
{
    public class BaseModel
    {
        //we have to use has a relationship here as oposed to is a because using CloudBlockBlob as a base class requires
        //the constructor to pass a URI and the primary behavior of the blob class is navigating between data and json blob types
        //using the hash value to retrieve the URL.
        public CloudBlockBlob AzureBlob { get; set; }
        public ILogger Log { get; set; }

        // encapsulates the GetBlobByHash behavior which is reused between both DataBlob and JsonBlob subclasses.
        public BaseModel() { }

        [Obsolete("Use ModelExtensions method")]
        public static string CalculateMD5Hash(string input)
        {
            return input.CalculateMD5Hash();
        }
    }
}
