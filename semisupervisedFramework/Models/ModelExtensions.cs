using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace semisupervisedFramework.Models
{
    public static class ModelExtensions
    {
        public static string CalculateMD5Hash(this string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var hash = MD5.Create().ComputeHash(bytes);
            var hex = hash.Select(x => x.ToString("X2"));
            return string.Join(string.Empty, hex);
        }

        public static JsonModel GetJsonModel(this string md5Hash)
        {
            var search = new Search();
            return search.CommitSearch(md5Hash).ToJsonModel();
        }

        public static JsonModel ToJsonModel(this JObject json)
        {
            return new JsonModel()
            {
                Id = json.GetToken<string>("id"),
                Labels = json.GetToken<IList<string>>("labels"),
                BlobInfo = new BlobModel
                {
                    Md5Hash = json.GetToken<string>("blobInfo.hash"),
                    Modified = json.GetToken<DateTime>("blobInfo.modified"),
                    Name = json.GetToken<string>("blobInfo.name"),
                    Url = json.GetToken<string>("blobInfo.url"),
                },
            };
        }

        private static T GetToken<T>(this JObject json, string name)
        {
            var result = json.SelectToken("id")?.ToString() ?? throw new MissingRequiredObjectException(name);
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)DateTime.Parse(result, CultureInfo.InvariantCulture);
            }
            else
            {
                return JsonConvert.DeserializeObject<T>(result);
            }
        }
    }
}
