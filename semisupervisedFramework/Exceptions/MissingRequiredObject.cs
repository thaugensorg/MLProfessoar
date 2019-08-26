using System;

namespace semisupervisedFramework
{
    public class MissingRequiredObjectException : Exception
    {
        public MissingRequiredObjectException(string message)
            : base(message)
        {
        }
    }
}
