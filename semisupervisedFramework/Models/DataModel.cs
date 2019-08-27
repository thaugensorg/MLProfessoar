
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage.Blob;

namespace semisupervisedFramework.Models
{
    //This class encapsulates the fucntionality for the data blob files that will be used to train the semisupervised model.
    public class DataModel : BaseModel
    {
        public JsonModel JsonModel { get; set; }
    }
}
