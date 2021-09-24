## LNDroneController - C# Lightning node automation framework
#### A Plebnet Playground Project
---
- Control a fleet of nodes automatically. 
- First pass will cover LND daemons, once basic coverage is achieved will add c-lightning functionality
  

## drone-config.json file format
Below is example config. LocalIPPath is not required, but used if provided to read a file with local IP address and map a ClearnetConnectionString if not being directly adversited via GetInfo LND call. 
  ```
[
    {
        "TlsCertFilePath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_0/tls.cert",
        "MacaroonFilePath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_0/data/chain/bitcoin/signet/admin.macaroon",
        "Host": "playground-lnd-0:10009",
        "LocalIPPath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_0/localhostip",
    },
    {
        "TlsCertFilePath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_1/tls.cert",
        "MacaroonFilePath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_1/data/chain/bitcoin/signet/admin.macaroon",
        "Host": "playground-lnd-1:10009",
        "LocalIPPath": "/path/to/plebnet-playground-cluster/volumes/lnd_datadir_1/localhostip",
    }
]
  ```