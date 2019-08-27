using System;
using Microsoft.Azure.Search;

namespace semisupervisedFramework.Models
{
    public class SearchModel
    {
        public string Name { get; set; }

        public Uri Url { get; set; }

        [IsSearchable]
        public string Md5Hash { get; set; }

        public DateTimeOffset Modified { get; set; }

        public override string ToString()
        {
            return $"Name: {Name}\tURL: {Url}\tHash: {Md5Hash}\tModified: {Modified}";
        }
    }
}
