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

namespace semisupervisedFramework.Storage
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

        public static CloudBlockBlob GetCloudBlockBlob(this Model model)
        {
            var account = new Storage.Helper().GetStorageAccount();
            var client = account.CreateCloudBlobClient();
            return new CloudBlockBlob(model.Search.Url, client);
        }

        public static Model ToStorageModel(this string md5Hash)
        {
            var result = new Search().CommitSearch(md5Hash).ToStorageModel();
            result.Md5Hash = md5Hash;
            return result;
        }

        private static Model ToStorageModel(this JObject json)
        {
            return new Model()
            {
                Id = json.GetJObjectToken<string>("id"),
                Labels = json.GetJObjectToken<IList<string>>("labels"),
                Search = new Model.SearchInfo
                {
                    Md5Hash = json.GetJObjectToken<string>("blobInfo.hash"),
                    Modified = json.GetJObjectToken<DateTime>("blobInfo.modified"),
                    Name = json.GetJObjectToken<string>("blobInfo.name"),
                    Url = json.GetJObjectToken<Uri>("blobInfo.url"),
                }
            };
        }

        private static T GetJObjectToken<T>(this JObject json, string name)
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
