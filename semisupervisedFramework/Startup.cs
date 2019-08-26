using Microsoft.Azure.WebJobs.Host.Config;

namespace semisupervisedFramework
{
    public class Startup : IExtensionConfigProvider
    {
        // *****todo***** why doesn't this run???
        public void Initialize(ExtensionConfigContext context) => Search.InitializeSearch();
    }
}
