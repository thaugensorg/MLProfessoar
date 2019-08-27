using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Azure.Storage.Blob;
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

        public static CloudBlockBlob GetCloudBlockBlob(this JsonModel model)
        {
            var account = new Engine().GetStorageAccount();
            var client = account.CreateCloudBlobClient();
            return new CloudBlockBlob(model.SearchInfo.Url, client);
        }

        public static DataModel ToDataModel(this JsonModel model)
        {
            return new DataModel
            {
                JsonModel = model,
                AzureBlob = model.GetCloudBlockBlob()
            };
        }

        public static JsonModel ToJsonModel(this string md5Hash)
        {
            var search = new Search();
            return search.CommitSearch(md5Hash).ToJsonModel();
        }

        private static JsonModel ToJsonModel(this JObject json)
        {
            return new JsonModel()
            {
                Id = json.GetToken<string>("id"),
                Labels = json.GetToken<IList<string>>("labels"),
                SearchInfo = json.ToSearchModel(),
            };
        }

        private static SearchModel ToSearchModel(this JObject json)
        {
            return new SearchModel
            {
                Md5Hash = json.GetToken<string>("blobInfo.hash"),
                Modified = json.GetToken<DateTime>("blobInfo.modified"),
                Name = json.GetToken<string>("blobInfo.name"),
                Url = json.GetToken<Uri>("blobInfo.url"),
            };
        }

        private static T GetToken<T>(this JObject json, string name)
        {
            var result = json.SelectToken("id")?.ToString() ?? throw new MissingRequiredObjectException(name);
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)DateTime.Parse(result, CultureInfo.InvariantCulture);
            }
            else if (typeof(T) == typeof(Uri))
            {
                return (T)(object)new Uri(result);
            }
            else
            {
                return JsonConvert.DeserializeObject<T>(result);
            }
        }
    }
}
