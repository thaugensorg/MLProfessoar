using System;

namespace semisupervisedFramework.Settings
{
    public static class SettingsHelper
    {
        public static bool TryRead(this string key, out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;

            try
            {
                if (!Environment.GetEnvironmentVariables().Contains(key))
                {
                    error = $"Environment variable [{key}] not found.";
                }
                else if (string.IsNullOrEmpty(value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process)))
                {
                    error = $"Environment variable [{key}] empty.";
                }
                return string.IsNullOrEmpty(error);
            }
            catch (Exception e)
            {
                error = $"{e}/{e.Message}";
                return false;
            }
        }
    }
}
