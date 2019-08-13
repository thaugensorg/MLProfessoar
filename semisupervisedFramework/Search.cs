using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Spatial;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

// http://danielcoding.net/working-with-azure-search-in-c-net/
// https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk

namespace semisupervisedFramework
{
    class Search
    {
        // This sample shows how to delete, create, upload documents and query an index
        static void InitializeSearch(ILogger log)
        {
            string SearchApiKey = Environment.GetEnvironmentVariable("blobSearchKey", log);
            string indexName = Environment.GetEnvironmentVariable("bindinghash", log);

            SearchServiceClient serviceClient = new SearchServiceClient("semisupervisedBlobSearch", new SearchCredentials(SearchApiKey));

            Console.WriteLine("{0}", "Deleting index...\n");
            indexClient = serviceClient.Indexes(indexName);
                
                DeleteIndexIfExists(indexName, serviceClient);

            Console.WriteLine("{0}", "Creating index...\n");
            serviceClient.CreateIndex(indexName, serviceClient);

            ISearchIndexClient indexClient = serviceClient.Indexes.GetClient(indexName);

            Console.WriteLine("{0}", "Uploading documents...\n");
            UploadDocuments(indexClient);

            ISearchIndexClient indexClientForQueries = CreateSearchIndexClient(configuration);

            RunQueries(indexClientForQueries);

            Console.WriteLine("{0}", "Complete.  Press any key to end application...\n");
            Console.ReadKey();
        }
    }
    public class DataBlob
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Url { get; set; }
        public string Hash { get; set; }
        public DateTimeOffset Modified { get; set; }

        public override string ToString()
        {
            return $"Id: {Id}\tName: {Name}\tURL: {Url}\tHash: {Hash}\tModified: {Modified}";
        }
    }
    public static class Helper
    {
        public static SearchServiceClient Initialize(string serviceName, string indexName, string apiKey)
        {
            SearchServiceClient serviceClient = new SearchServiceClient(serviceName, new SearchCredentials(apiKey));
            DeleteIfIndexExist(serviceClient, indexName);
            CreateIndex(serviceClient, indexName);

            return serviceClient;
        }

        private static void CreateIndex(SearchServiceClient client, string indexName)
        {
            // https://docs.microsoft.com/en-us/rest/api/searchservice/create-index
            var indexDefinition = new Index()
            {
                Name = indexName,
                Fields = new[]
                {
                    new Field("id", DataType.String)                  { IsKey = true},
                    new Field("name", DataType.String)                { IsRetrievable = true},
                    new Field("url", DataType.String)                 { IsRetrievable = true},
                    new Field("hash", DataType.String)                { IsSearchable = true},
                    new Field("modified", DataType.DateTimeOffset)    { IsRetrievable = true},
                }
            };

            client.Indexes.Create(indexDefinition);
        }

        private static void DeleteIfIndexExist(SearchServiceClient client, string indexName)
        {
            if (client.Indexes.Exists(indexName))
            {
                client.Indexes.Delete(indexName);
            }
        }
    }
    public class Uploader
    {
        private List<DataBlob> PrepareDocuments()
        {
            List<DataBlob> carDocuments = new List<DataBlob>();

            DataBlob c1 = new DataBlob()
            {
                Id = "1",
                Name = "Proton Iriz",
                Category = "Hatchback",
                LaunchDate = new DateTimeOffset(2015, 1, 20, 0, 0, 0, TimeSpan.Zero),
                Price = 55000,
                SafetyRating = 5
            };

            DataBlob c2 = new DataBlob()
            {
                Id = "2",
                Name = "Perodua Myvi",
                Category = "Hatchback",
                LaunchDate = new DateTimeOffset(2004, 6, 15, 0, 0, 0, TimeSpan.Zero),
                Price = 40000,
                SafetyRating = 3
            };

            DataBlob c3 = new DataBlob()
            {
                Id = "3",
                Name = "Perodua Axia",
                Category = "Hatchback",
                LaunchDate = new DateTimeOffset(2014, 12, 25, 0, 0, 0, TimeSpan.Zero),
                Price = 30000,
                SafetyRating = 2
            };

            DataBlob c4 = new DataBlob()
            {
                Id = "4",
                Name = "BMW 320i Sport",
                Category = "Sedan",
                LaunchDate = new DateTimeOffset(2000, 8, 31, 0, 0, 0, TimeSpan.Zero),
                Price = 300000,
                SafetyRating = 4
            };

            carDocuments.Add(c1);
            carDocuments.Add(c2);
            carDocuments.Add(c3);
            carDocuments.Add(c4);

            return carDocuments;
        }

        public void Upload(ISearchIndexClient indexClient)
        {
            try
            {
                var documents = PrepareDocuments();
                var batch = IndexBatch.Upload(documents);
                indexClient.Documents.Index(batch);

                Thread.Sleep(2000);
            }
            catch (IndexBatchException e)
            {
                Console.WriteLine(
                    $"Oops! The following index failed...\n { e.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key) }");
            }
        }

    }
}
