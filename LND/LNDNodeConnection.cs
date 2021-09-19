using Grpc.Core;
using Lnrpc;
using Routerrpc;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using ServiceStack;
using System.Security.Cryptography;

namespace LNDroneController.LND
{
    public class LNDNodeConnection
    {
        private Grpc.Core.Channel gRPCChannel;
        private Lightning.LightningClient LightningClient;
        private Router.RouterClient RouterClient;

        public string LocalNodePubKey { get; private set; }
        public string LocalAlias { get; private set; }
        public string ClearnetConnectString { get; private set; }
        public string OnionConnectString { get; private set; }

        public void Start(string tlsCertFilePath, string macoroonFilePath, string host, string localIP = null)
        {
            // Due to updated ECDSA generated tls.cert we need to let gprc know that
            // we need to use that cipher suite otherwise there will be a handshake
            // error when we communicate with the lnd rpc server.
            System.Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

            var cert = System.IO.File.ReadAllText(tlsCertFilePath);
            var sslCreds = new SslCredentials(cert);

            byte[] macaroonBytes = System.IO.File.ReadAllBytes(macoroonFilePath);
            var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", ""); // hex format stripped of "-" chars


            // combine the cert credentials and the macaroon auth credentials using interceptors
            // so every call is properly encrypted and authenticated
            Task AddMacaroon(AuthInterceptorContext context, Metadata metadata)
            {
                metadata.Add(new Metadata.Entry("macaroon", macaroon));
                return Task.CompletedTask;
            }
            var macaroonInterceptor = new AsyncAuthInterceptor(AddMacaroon);
            var combinedCreds = ChannelCredentials.Create(sslCreds, CallCredentials.FromInterceptor(macaroonInterceptor));

            // finally pass in the combined credentials when creating a channel
            gRPCChannel = new Grpc.Core.Channel(host, combinedCreds);

            LightningClient = new Lnrpc.Lightning.LightningClient(gRPCChannel);
            RouterClient = new Routerrpc.Router.RouterClient(gRPCChannel);
            var nodeInfo = LightningClient.GetInfo(new GetInfoRequest());
            LocalNodePubKey = nodeInfo.IdentityPubkey;
            LocalAlias = nodeInfo.Alias;
            ClearnetConnectString = nodeInfo.Uris.FirstOrDefault(x => !x.Contains("onion"));
            OnionConnectString = nodeInfo.Uris.FirstOrDefault(x => x.Contains("onion"));
            if (ClearnetConnectString.IsNullOrEmpty() && !localIP.IsNullOrEmpty()) //hacky override
            {
                ClearnetConnectString = $"{nodeInfo.IdentityPubkey}@{localIP}:9735";
            }
        }

        public async Task Stop()
        {
            await gRPCChannel.ShutdownAsync();
        }
        public async Task<GetInfoResponse> GetInfo()
        {
            return await LightningClient.GetInfoAsync(new GetInfoRequest());  //Get node info
        }

        public async Task<ConnectPeerResponse> Connect(string connectionString, bool perm = false)
        {
            var addr = new LightningAddress();
            var splitConnection = connectionString.Split('@');
            addr.Host = splitConnection[1];
            addr.Pubkey = splitConnection[0];
            return await LightningClient.ConnectPeerAsync(new ConnectPeerRequest{Addr =  addr, Perm = perm}); 
        }

        public async Task<ChannelPoint> OpenChannel(string publicKey, long localFundingAmount, long pushSat = 0, ulong satPerVbyte = 1)
        {
              var response = await LightningClient.OpenChannelSyncAsync(new OpenChannelRequest{
                  LocalFundingAmount = localFundingAmount,
                  NodePubkey = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(publicKey)),
                  SatPerVbyte = satPerVbyte,
                  SpendUnconfirmed = true,
                  PushSat = pushSat,
              });
              return response;
        }
        public async Task<List<Lnrpc.Channel>> GetChannels()
        {
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest());
            return response.Channels.ToList();
        }
        public async Task<SendResponse> SendMessage(string dest, string message)
        {
            //34349334 - message
            //const keySendValueType = '5482373484';
            //const createSecret = () => randomBytes(32).toString('hex');
             var r = new Random();
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var randomHexString = Convert.ToHexString(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(randomBytes); 
            var payment = new SendRequest{
                Dest = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(dest)),
                Amt = 10,
                FeeLimit = new FeeLimit{Fixed = 10},
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
            };
            payment.DestCustomRecords.Add(5482373484,Google.Protobuf.ByteString.CopyFrom(randomBytes));
            payment.DestCustomRecords.Add(34349334,Google.Protobuf.ByteString.CopyFrom(Encoding.UTF8.GetBytes(message)));
            
            var response = await LightningClient.SendPaymentSyncAsync(payment);
            return response;
        }

        public async Task<Payment> SendMessageV2(string dest, string message)
        {
            //34349334 - message
            //const keySendValueType = '5482373484';
            //const createSecret = () => randomBytes(32).toString('hex');
             var r = new Random();
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var randomHexString = Convert.ToHexString(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(randomBytes); 
            var payment = new SendPaymentRequest{
                Dest = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(dest)),
                Amt = 10,
                FeeLimitSat = 10,
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
                TimeoutSeconds= 60,
            };
            payment.DestCustomRecords.Add(5482373484,Google.Protobuf.ByteString.CopyFrom(randomBytes));
            payment.DestCustomRecords.Add(34349334,Google.Protobuf.ByteString.CopyFrom(Encoding.UTF8.GetBytes(message)));
            var streamingCallResponse = RouterClient.SendPaymentV2(payment);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
        }
        public async Task RunHTLCLoop(CancellationToken cancellationToken)
        {
            var info = await LightningClient.GetInfoAsync(new GetInfoRequest());  //Get node info

            var htlcEventTask = Task.Run(async () =>
            {
                var routerClient = new Routerrpc.Router.RouterClient(gRPCChannel);
                using (var htlcEventStream = routerClient.SubscribeHtlcEvents(new SubscribeHtlcEventsRequest()))
                {
                    while (await htlcEventStream.ResponseStream.MoveNext())
                    {
                        var htlcEvent = htlcEventStream.ResponseStream.Current;
                        var incomingNodeName = await GetFriendlyNodeNameFromChannel(htlcEvent.IncomingChannelId);
                        var outgoingNodeName = await GetFriendlyNodeNameFromChannel(htlcEvent.OutgoingChannelId);
                    }
                }
            }, cancellationToken);

            await htlcEventTask;
        }

        private async Task<string> GetFriendlyNodeNameFromChannel(ulong channelId)
        {
            if (channelId == 0)
                return string.Empty;
            var channel = await LightningClient.GetChanInfoAsync(new ChanInfoRequest { ChanId = channelId });
            var node = await LightningClient.GetNodeInfoAsync(new NodeInfoRequest { PubKey = GetRemoteNodePubKeyFromChannel(channel) });
            return node.Node.Alias;
        }

        private string GetRemoteNodePubKeyFromChannel(ChannelEdge edge)
        {
            return edge.Node1Pub == LocalNodePubKey ? edge.Node2Pub : edge.Node1Pub;
        }
    }
}
