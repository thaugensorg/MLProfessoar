using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;

namespace semisupervisedFramework.Settings
{
    public class Settings
    {
        private readonly ILogger _logger;
        public Settings(ILogger logger)
        {
            _logger = logger;
        }

        public string Something => Read();
        public string AzureWebJobsStorageConnectionString => Read();
        public string ModelServiceEndpoint => Read();
        public string ModelAssetParameterName => Read();
        public string PendingEvaluationStorageContainerName => Read();
        public string EvaluatedDataStorageContainerName => Read();
        public string PendingSupervisionStorageContainerName => Read();
        public string LabeledDataStorageContainerName => Read();
        public string ModelValidationStorageContainerName => Read();
        public string PendingNewModelStorageContainerName => Read();
        public string ConfidenceJSONPath => Read();
        public double ConfidenceThreshold => Read<double>();
        public double ModelVerificationPercentage => Read<double>();
        public string BlobSearchKey => Read();
        public string Bindinghash => Read();
        public string AzureWebJobsStorage => Read();
        public string SearchServiceName => Read();
        public string JsonStorageContainerName => Read();
        public string DataTagsBlobName => Read();
        public string DataTagsFileHash => Read();
        public string TagsUploadServiceEndpoint => Read();
        public string TagDataParameterName => Read();
        public string LabeledDataServiceEndpoint => Read();
        public string ReadModelAssetParameterName => Read();

        public T Read<T>([CallerMemberName]string key = null)
        {
            var value = Read(key);
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public string Read([CallerMemberName]string key = null)
        {
            if (key.TryRead(out var value, out var error))
            {
                return value;
            }
            else
            {
                _logger.LogError(error);
                throw new Exception(error);
            }
        }
    }
}
