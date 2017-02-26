using System;
using System.Threading.Tasks;
using EtcdNet;
using System.Linq;
using System.Threading;

namespace ConsoleApplication
{
    public class Program
    {


        public static void Main(string[] args)
        {
            var shutdownToken = new CancellationTokenSource();
            var election = new ElectionRunner(
                shutdownToken.Token,
                isNowMaster: async (cancellationToken) =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("We're Master!!");
                        Task.Delay(TimeSpan.FromSeconds(3)).Wait();

                        var distLock = new DistributedLock();
                        using (var resLock = await distLock.GetLock("exampleResource", 15))
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            await resLock.RenewLock();
                            await Task.Delay(TimeSpan.FromSeconds(10));                            
                        }
                    }
                },
                isNowSecondary: (cancellationToken) =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("We're secondary");
                        Task.Delay(TimeSpan.FromSeconds(3)).Wait();
                    }
                },
                electionTimeoutSec: 15);

            Task.Run(election.StartParticipatingInElectionAsync).Wait();



            Console.WriteLine("Hello World!");
        }


    }
}
