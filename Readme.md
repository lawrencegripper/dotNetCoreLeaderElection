# Notes

Download and run etcd, code uses localhost currently but could be updated to use a cluster. 

## Running

    dotnet restore
    dotnet run 

If you run multiple processes only one will be master. The longest running process should always assume the master role. Should it 
fail the next longest running node will then become the master after the 15sec TTL. 