using System;
using System.Threading.Tasks;
using EtcdNet;
using System.Linq;

namespace ConsoleApplication
{
    public class Program
    {
        private const string ElectionKey = "/MasterElection/Status";
        private const int ElectionTimeoutSec = 15;
        private static string InstanceId = Guid.NewGuid().ToString();
        private static EtcdClient _etcdClient;

        public static void Main(string[] args)
        {

            Task.Run(async () =>
            {
                var options = new EtcdClientOpitions()
                {
                    Urls = new string[] { "http://localhost:2379" }
                };
                _etcdClient = new EtcdClient(options);

                //Create our InOrder key under the election key. 
                // If we're the first node we'll get /MasterElection/Status/1, second /MasterElection/Status/2 (roughly speaking)
                var instanceElectionResponse = await _etcdClient.CreateInOrderNodeAsync(ElectionKey, InstanceId, ElectionTimeoutSec);

                while (true)
                {
                    var isMaster = await UpdateKeyAndCheckIsMaster(instanceElectionResponse.Node);

                    Console.WriteLine($"Is master: {isMaster}");
                }

            }).GetAwaiter().GetResult();

            Console.WriteLine("Hello World!");
        }

        private static async Task<bool> UpdateKeyAndCheckIsMaster(EtcdNode node)
        {
            //Update our key to ensure it doens't expire
            await _etcdClient.SetNodeAsync(node.Key, InstanceId, ElectionTimeoutSec);

            await Task.Delay(TimeSpan.FromSeconds(ElectionTimeoutSec - 10));

            //Get a sorted list of nodes for the election Key. 
            //Oldest nodes will be a at the top. They're a good candidate for master as they're the most stable. 
            var currentStatus = await _etcdClient.GetNodeAsync(ElectionKey, false, false, true);

            //The first node is the oldest, if this is us - we're the master
            if (currentStatus.Node.Nodes.First().Value == InstanceId)
            {
                return true;
            }

            return false;
        }
    }
}
