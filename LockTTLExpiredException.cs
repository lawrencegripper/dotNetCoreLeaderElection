using System;

namespace ConsoleApplication
{
    internal class LockTTLExpiredException : Exception
    {
        public LockTTLExpiredException()
        {
        }

        public LockTTLExpiredException(string message) : base(message)
        {
        }

        public LockTTLExpiredException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}