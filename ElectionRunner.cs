using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EtcdNet;

namespace ConsoleApplication
{
    public class ElectionRunner
    {
        private EtcdClient etcdClient;
        private const int electionTimeoutSec = 15;
        private readonly CancellationToken cancellationToken;
        private Task electionTask; 
        private CancellationTokenSource electionTaskCancellationSource = new CancellationTokenSource();

        public string InstanceId { get; private set; } = Guid.NewGuid().ToString();
        public string ElectionKey { get; private set; } = "/MasterElection/Status";
        public Action<CancellationToken> isNowMaster { get; private set; }
        public Action<CancellationToken> isNowSecondary { get; private set; }
        private bool isMaster = false;
        public bool IsMaster
        {
            get
            {
                return isMaster;
            }
        }

        public ElectionRunner(CancellationToken cancellationToken, Action<CancellationToken> isNowMaster, Action<CancellationToken> isNowSecondary)
        {
            this.cancellationToken = cancellationToken;
            this.isNowMaster = isNowMaster;
            this.isNowSecondary = isNowSecondary;

            var options = new EtcdClientOpitions()
            {
                Urls = new string[] { "http://localhost:2379" }
            };
            etcdClient = new EtcdClient(options);
        }

        public async Task StartParticipatingInElectionAsync()
        {
            await Task.Run(async () =>
            {
                //Create our InOrder key under the election key. 
                // If we're the first node we'll get /MasterElection/Status/1, second /MasterElection/Status/2 (roughly speaking)
                var instanceElectionResponse = await etcdClient.CreateInOrderNodeAsync(ElectionKey, InstanceId, electionTimeoutSec);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var shouldBeMaster = await UpdateKeyAndCheckIsMaster(instanceElectionResponse.Node);

                    Debug.WriteLine($"Is master: {shouldBeMaster}");

                    if (shouldBeMaster)
                    {
                        
                        if (IsMaster)
                        {
                            Debug.WriteLine($"Was already master, nothing changes");
                        }
                        else
                        {
                            isMaster = true;
                            await CancelCurrentElectionTaskAsync();
                            electionTask = Task.Run(() => isNowMaster(electionTaskCancellationSource.Token));
                            Debug.WriteLine($"Becoming master, wasn't already");
                        }
                    }
                    else
                    {
                        if (!IsMaster)
                        {
                            Debug.WriteLine($"Was already secondary, nothing changes");
                        }
                        else
                        {
                            isMaster = false;
                            await CancelCurrentElectionTaskAsync();
                            electionTask = Task.Run(() => isNowSecondary(electionTaskCancellationSource.Token));
                            Debug.WriteLine($"Not the master, either lost it or never had it");
                        }
                    }
                }
            });

        }



        private async Task CancelCurrentElectionTaskAsync()
        {
            if (electionTask == null)
            {
                return;
            }

            electionTaskCancellationSource?.Cancel();

            await Task.Delay(TimeSpan.FromSeconds(5));

            if (!electionTask.IsCanceled)
            {
                throw new FailedToCancelElectionTask();
            }

            electionTaskCancellationSource = new CancellationTokenSource();
        }

        private async Task<bool> UpdateKeyAndCheckIsMaster(EtcdNode node)
        {
            //Update our key to ensure it doens't expire
            await etcdClient.SetNodeAsync(node.Key, InstanceId, electionTimeoutSec);

            await Task.Delay(TimeSpan.FromSeconds(electionTimeoutSec - 10));

            //Get a sorted list of nodes for the election Key. 
            //Oldest nodes will be a at the top. They're a good candidate for master as they're the most stable. 
            var currentStatus = await etcdClient.GetNodeAsync(ElectionKey, false, false, true);

            //The first node is the oldest, if this is us - we're the master
            if (currentStatus.Node.Nodes.First().Value == InstanceId)
            {
                return true;
            }

            return false;
        }

    }
}