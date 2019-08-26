using System;

namespace semisupervisedFramework.Exceptions
{
    public class ZeroLengthFileException : Exception
    {
        public ZeroLengthFileException(string message)
            : base(message)
        {
        }
    }
}
