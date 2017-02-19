using System;

namespace ConsoleApplication
{
    internal class FailedToCancelElectionTask : Exception
    {
        public FailedToCancelElectionTask()
        {
        }

        public FailedToCancelElectionTask(string message) : base(message)
        {
        }

        public FailedToCancelElectionTask(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}