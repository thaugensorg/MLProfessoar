using System;
using System.Collections.Generic;
using System.Buffers;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;

using Newtonsoft.Json;

namespace semisupervisedFramework.Models
{
    //This class encapsulates the functionality for the json blob files that are bound to the data files.  These json files contain all of the meta data, labeling data, and results of every evaluation against the model
    public class JsonModel : BaseModel
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }

        public SearchModel SearchInfo { get; set; }

        public IList<string> Labels { get; set; }
    }
}
