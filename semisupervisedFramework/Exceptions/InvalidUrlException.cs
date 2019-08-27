using System;

namespace semisupervisedFramework.Exceptions
{
    public class InvalidUrlException : Exception
    {
        public InvalidUrlException(string message)
            : base(message)
        {
        }
    }
}
