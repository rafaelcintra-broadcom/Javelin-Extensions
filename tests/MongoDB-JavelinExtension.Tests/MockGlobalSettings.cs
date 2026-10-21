using System;

namespace GridTools.Javelin.Common
{
    public static class GlobalSettings
    {
        public static class MessageLogger
        {
            public static void LogMessage(string message, params object[] args)
            {
                // No-op for unit tests or Console output
                #if DEBUG
                Console.WriteLine(string.Format(message, args));
                #endif
            }
        }
    }

    public class JavelinPasswordDecryptor
    {
        public string SubstitutePasswordsInString(string input)
        {
            return input;
        }
    }
}
