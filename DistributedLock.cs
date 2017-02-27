using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EtcdNet;
using static EtcdNet.EtcdCommonException;

namespace ConsoleApplication
{
    public class DistributedLock
    {
        private const string LockPrefix = "/DistributedLock/locks/";
        private readonly EtcdClient EtcdClient;
        private Guid InstanceId = Guid.NewGuid();
        public DistributedLock()
        {
            var options = new EtcdClientOpitions()
            {
                Urls = new string[] { "http://localhost:2379" }
            };
            EtcdClient = new EtcdClient(options);
        }

        public async Task<DistributedLockSession> GetLock(string lockName, int ttlInSecs = 45)
        {
            var compositeKey = LockPrefix + lockName;
            var lockInstanceId = Guid.NewGuid();
            try
            {
                //Try and create a node for the lock
                var response = await EtcdClient.CreateNodeAsync(compositeKey, lockInstanceId.ToString(), ttlInSecs);
                return new DistributedLockSession(EtcdClient, compositeKey, lockInstanceId, ttlInSecs);
            }
            catch (NodeExist ex)
            {
                Debug.WriteLine(ex.ToString());
                //If it already exists then someone else is holding the lock. 
                //Use watch to wait for changes to the node
                var watchResult = await EtcdClient.WatchNodeAsync(compositeKey);
                //If it's been removed try and get a lock again 
                if (watchResult.Action == "delete" || watchResult.Action == "expire")
                {
                    return await GetLock(lockName, ttlInSecs);
                }
                else
                {
                    throw new Exception($"Unknown action returned by watch statement: {watchResult.Action}");
                }
            }

        }

        public class DistributedLockSession : IDisposable
        {
            private readonly int ttlInSecs;
            private readonly string key;
            private readonly EtcdClient client;
            private readonly Guid lockInstanceId;
            private DateTime expireTime;
            private bool isReleased = false;
            private const int ttlBufferSec = 2;
            private Task lockCheckTask;
            private CancellationTokenSource lockCheckCancellation = new CancellationTokenSource();

            public DistributedLockSession(EtcdClient client, string key, Guid lockInstanceId, int ttlInSecs = 15)
            {
                this.expireTime = DateTime.Now.Add(TimeSpan.FromSeconds(ttlInSecs));

                this.client = client;
                this.key = key;
                this.ttlInSecs = ttlInSecs;
                this.lockInstanceId = lockInstanceId;

                lockCheckTask = Task.Run(async () =>
                {
                    while (true)
                    {
                        //Arbritrary Tick time... could be less. Should ok if you're lock TTL is > 3sec but worth testing
                        await Task.Delay(TimeSpan.FromSeconds(1));

                        //Very simple calc to auto rety if we're 3/4's through the ttl length. 
                        //Todo: Will be a problem if the ETCD CompareAndSwap takes longer than 1/4 of TTL Time. 
                        if (AboutToExpire(ttlInSecs))
                        {
                            await RenewLock();
                        }

                        if (lockCheckCancellation.Token.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                });
            }

            private bool AboutToExpire(int ttlInSecs)
            {
                return DateTime.Now > expireTime.Add(TimeSpan.FromSeconds((ttlInSecs / 4) * -1));
            }

            public async Task Release()
            {
                if (isReleased)
                {
                    throw new Exception("Lock is no longer held");
                }

                await client.CompareAndDeleteNodeAsync(key, lockInstanceId.ToString());
                this.isReleased = true;
            }

            public async Task RenewLock()
            {
                if (isReleased)
                {
                    throw new Exception("Lock is no longer held");
                }
                //Renew the key with an exteneded ttlInSecs
                //Check the value is out lock Instance to ensure we still hold the lock. 
                await client.CompareAndSwapNodeAsync(key, lockInstanceId.ToString(), lockInstanceId.ToString(), ttlInSecs);
                expireTime = DateTime.Now.Add(TimeSpan.FromSeconds(ttlInSecs));
            }

            #region IDisposable Support

            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        //Cleanup the lock and cancel the lockCheckTask
                        Release().GetAwaiter().GetResult();
                        isReleased = true;
                        lockCheckCancellation.Cancel();
                    }

                    disposedValue = true;
                }
            }

            // This code added to correctly implement the disposable pattern.
            void IDisposable.Dispose()
            {
                // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
                Dispose(true);
                // TODO: uncomment the following line if the finalizer is overridden above.
                // GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}