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
        private Timer LockExpiredTimer;

        public DistributedLock()
        {
            var options = new EtcdClientOpitions()
            {
                Urls = new string[] { "http://localhost:2379" }
            };
            EtcdClient = new EtcdClient(options);
        }

        public async Task<DistributedLockSession> GetLock(string lockName, int ttlInSecs = 15)
        {
            var compositeKey = LockPrefix + lockName;
            var lockInstanceId = Guid.NewGuid();
            try
            {
                var response = await EtcdClient.CompareAndSwapNodeAsync(compositeKey, "nil", lockInstanceId.ToString(), ttlInSecs);
            }
            catch (TestFailed ex)
            {
                await EtcdClient.WatchNodeAsync(compositeKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return new DistributedLockSession(EtcdClient, compositeKey, lockInstanceId, ttlInSecs);
        }

        public class DistributedLockSession : IDisposable
        {
            private readonly int ttlInSecs;
            private readonly string key;
            private readonly EtcdClient client;
            private readonly Guid lockInstanceId;
            private readonly Timer expireTimer;
            private bool isReleased = false;

            public DistributedLockSession(EtcdClient client, string key, Guid lockInstanceId, int ttlInSecs = 15)
            {
                this.client = client;
                this.key = key;
                this.ttlInSecs = ttlInSecs;
                this.lockInstanceId = lockInstanceId;
                this.expireTimer = new Timer(x =>
                {
                    throw new Exception("Lock expired");
                }, this, ttlInSecs * 100, -1);
            }

            public async Task Release()
            {
                await client.CompareAndDeleteNodeAsync(key, lockInstanceId.ToString());
                this.expireTimer.Dispose();
                this.isReleased = true;
            }

            public async Task RenewLock()
            {
                if (isReleased)
                {
                    throw new Exception("Lock is no longer held");
                }
                await client.CompareAndSwapNodeAsync(key, lockInstanceId.ToString(), lockInstanceId.ToString(), ttlInSecs);
            }

            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        
                        Release().GetAwaiter().GetResult();
                        isReleased = true;
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