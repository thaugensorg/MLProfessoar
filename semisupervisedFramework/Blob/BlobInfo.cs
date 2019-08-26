using System;
using Microsoft.Azure.Search;

namespace semisupervisedFramework.Blob
{
    public class BlobInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }

        [IsSearchable]
        public string Md5Hash { get; set; }
        public DateTimeOffset Modified { get; set; }

        public override string ToString()
        {
            return $"Name: {Name}\tURL: {Url}\tHash: {Md5Hash}\tModified: {Modified}";
        }
    }
}
