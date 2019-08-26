using System;

namespace semisupervisedFramework.Exceptions
{
    public class EnvironmentVariableNotSetException : Exception
    {
        public EnvironmentVariableNotSetException(string message)
            : base(message)
        {
        }
    }
}
