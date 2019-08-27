using System;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Search;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace semisupervisedFramework.Storage
{
    public sealed class Model
    {
        [Key]
        public string Id { get; set; }

        public IList<string> Labels { get; set; }

        //we have to use has a relationship here as oposed to is a because using CloudBlockBlob as a base class requires
        //the constructor to pass a URI and the primary behavior of the blob class is navigating between data and json blob types
        //using the hash value to retrieve the URL.
        public CloudBlockBlob GetAzureBlob() => this.GetCloudBlockBlob();

        public SearchInfo Search { get; set; }
        public string Md5Hash { get; set; }

        public class SearchInfo
        {
            public string Name { get; set; }
            public Uri Url { get; set; }
            [IsSearchable]
            public string Md5Hash { get; set; }
            public DateTimeOffset Modified { get; set; }
        }
    }
}
