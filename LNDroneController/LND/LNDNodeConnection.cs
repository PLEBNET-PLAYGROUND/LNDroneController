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
using Grpc.Net.Client;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Security.Policy;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace LNDroneController.LND
{
    public class LNDNodeConnection
    {
        private static Random r = new Random();
        private GrpcChannel gRPCChannel;
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

            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
            };
            var x509Cert = new X509Certificate2(tlsCertFilePath);
            httpClientHandler.ClientCertificates.Add(x509Cert);
            byte[] macaroonBytes = System.IO.File.ReadAllBytes(macoroonFilePath);
            var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", ""); // hex format stripped of "-" chars

            var credentials = CallCredentials.FromInterceptor((_, metadata) =>
            {
                metadata.Add(new Metadata.Entry("macaroon", macaroon));
                return Task.CompletedTask;
            });
            gRPCChannel = GrpcChannel.ForAddress(
                "https://" + host,
                new GrpcChannelOptions
                {
                    DisposeHttpClient = true, 
                    HttpHandler = httpClientHandler,
                    Credentials = ChannelCredentials.Create(new SslCredentials(), credentials),
                    MaxReceiveMessageSize = 128000000
                });

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
 
         
        public Task Stop()
        {
            gRPCChannel.Dispose();
            return Task.CompletedTask;
        }
        
        public async Task<GetInfoResponse> GetInfo()
        {
            return await LightningClient.GetInfoAsync(new GetInfoRequest());  //Get node info
        }
        
        /// <summary>
        /// List all active channels
        /// </summary>
        public async Task<List<Channel>> ListActiveChannels()
        {
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest{ActiveOnly = true});
            return response.Channels.ToList();
        }
        /// <summary>
        /// List all inactive channels
        /// </summary>
        public async Task<List<Channel>> ListInactiveChannels()
        {
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest{InactiveOnly = true});
            return response.Channels.ToList();
        }

        public async Task TryReconnect()
        {
            var inactive = await ListInactiveChannels();
            foreach (var chan in inactive)
            {
                var nodeInfo = await GetNodeInfo(chan.RemotePubkey);
                foreach (var addr in nodeInfo.Node.Addresses)
                {
                    try
                    {
                        var response = await LightningClient.ConnectPeerAsync(new ConnectPeerRequest
                        {
                            Addr = new LightningAddress
                            {
                                Host = addr.Addr,
                                Pubkey = nodeInfo.Node.PubKey,
                            },
                            Timeout = 10L,
                        });
                    }
                    catch (RpcException e) when (e.StatusCode == StatusCode.Unknown) {}
                    
                }
            }

            return;
        }

        private async Task<NodeInfo> GetNodeInfo(string pubkey, bool includeChannels = false)
        {
            return await LightningClient.GetNodeInfoAsync(new NodeInfoRequest
            {
                PubKey = pubkey,
                IncludeChannels = includeChannels
            });
        }

        /// <summary>
        /// List all channels fitting require query
        /// </summary>
        public async Task<List<Channel>> ListChannels(ListChannelsRequest req)
        {
            var response = await LightningClient.ListChannelsAsync(req);
            return response.Channels.ToList();
        }
        public async Task<ConnectPeerResponse> Connect(string connectionString, bool perm = false)
        {
            var addr = new LightningAddress();
            var splitConnection = connectionString.Split('@');
            addr.Host = splitConnection[1];
            addr.Pubkey = splitConnection[0];
            return await LightningClient.ConnectPeerAsync(new ConnectPeerRequest { Addr = addr, Perm = perm });
        }
        public async Task<DisconnectPeerResponse> Disconnect(string pubkey)
        {
            return await LightningClient.DisconnectPeerAsync(new DisconnectPeerRequest{PubKey = pubkey});
        }
        public async Task<ChannelPoint> OpenChannel(string publicKey, long localFundingAmount, long pushSat = 0, ulong satPerVbyte = 1)
        {
            var response = await LightningClient.OpenChannelSyncAsync(new OpenChannelRequest
            {
                LocalFundingAmount = localFundingAmount,
                NodePubkey = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(publicKey)),
                SatPerVbyte = satPerVbyte,
                SpendUnconfirmed = true,
                PushSat = pushSat,
            });
            return response;
        }
        public async Task<List<Channel>> GetChannels()
        {
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest());
            return response.Channels.ToList();
        }
        public async Task<Payment> ProbeKeysendPayment(string dest, long amount = 10, long maxFee = 10, int timeoutSeconds = 60)
        {
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash("not-gonna-match".ToUtf8Bytes());
            var payment = new SendPaymentRequest
            {
                Dest = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(dest)),
                Amt = amount,
                FeeLimitSat = maxFee,
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
                TimeoutSeconds = timeoutSeconds,
                AllowSelfPayment = true,
            };
            payment.DestCustomRecords.Add(5482373484, Google.Protobuf.ByteString.CopyFrom(randomBytes));  //keysend 
            var streamingCallResponse = RouterClient.SendPaymentV2(payment);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
        }
        public async Task<Route> ProbePaymentGirth(string dest, long maxAmount = 100000, long maxFee = 10, int timeoutSecond = 60)
        {
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash("not-gonna-match".ToUtf8Bytes());
            var findRoute = await ProbePayment(dest, 10, maxFee, timeoutSecond);
            if (findRoute.FailureReason == PaymentFailureReason.FailureReasonNoRoute)
            {
                Debug.Print($"No route for: {await GetNodeAliasFromPubKey(dest)}");
                return null;
            }
            Route nextRoute = findRoute.Htlcs.Last().Route;
            Route bestRoute = findRoute.Htlcs.Last().Route;
            if (findRoute.FailureReason.ToString() == "FailureReasonIncorrectPaymentDetails")
            {   //It's a good path
                long nextCurrent = maxAmount;
                int count = 0;
                Debug.Print($"Next Target:{nextCurrent}");
                while (true)
                {
                    count++;
                    try
                    {
                        var newRoute = new BuildRouteRequest
                        {
                            AmtMsat = nextCurrent * 1000L,
                            FinalCltvDelta = 40
                        };
                        foreach (Hop r in nextRoute.Hops)
                        {
                            newRoute.HopPubkeys.Add(Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(r.PubKey)));
                        }
                        var constructedRoute = await RouterClient.BuildRouteAsync(newRoute);
                        var nextAttempt = await RouterClient.SendToRouteV2Async(
                            new Routerrpc.SendToRouteRequest
                            {
                                Route = constructedRoute.Route,
                                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
                            }, deadline: DateTime.UtcNow.AddMinutes(1));
                        nextRoute = nextAttempt.Route;
                        switch (nextAttempt.Failure.Code)
                        {
                            case Failure.Types.FailureCode.IncorrectOrUnknownPaymentDetails:
                                return nextAttempt.Route;
                            case Failure.Types.FailureCode.TemporaryChannelFailure:
                                return bestRoute;
                            default:
                                //less?
                                nextCurrent = (long)(nextCurrent * 0.66);
                                break;
                        }
                        if (count == 15)
                        {
                            return bestRoute;
                        }

                    }
                    catch (RpcException e)
                    {
                        if (e.Status.Detail.Contains("no matching outgoing channel available"))
                        {
                            //less?
                            nextCurrent = (long)(nextCurrent * 0.75);
                        }
                        else
                        {
                            e.PrintDump();
                        }
                    }
                    Debug.Print($"Next Target:{nextCurrent}");
                }
            }
            return bestRoute;
        }
        public async Task<List<Route>> QueryRoutes(string dest, long amount = 10, long maxFee = 10, int timeoutSecond = 60, bool keySend = false)
        {
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var req = new QueryRoutesRequest
            {
                Amt = amount,
                PubKey = dest,
                FeeLimit = new FeeLimit { Fixed = maxFee },
            };
            if (keySend)
            {
                req.DestCustomRecords.Add(5482373484, Google.Protobuf.ByteString.CopyFrom(randomBytes));  //keysend
            }
            var queryRoutesResponse = await LightningClient.QueryRoutesAsync(req);
            return queryRoutesResponse.Routes.ToList();
        }
        public async Task<Payment> ProbePayment(string dest, long amount = 10, long maxFee = 10, int timeoutSeconds = 60)
        {
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash("not-gonna-match".ToUtf8Bytes());
            var payment = new SendPaymentRequest
            {
                Dest = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(dest)),
                Amt = amount,
                FeeLimitSat = maxFee,
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
                TimeoutSeconds = timeoutSeconds,
                AllowSelfPayment = true,

            };
            var streamingCallResponse = RouterClient.SendPaymentV2(payment);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
        }

        public async Task<DeleteAllPaymentsResponse> PurgePayments(DeleteAllPaymentsRequest request)
        {
            return await LightningClient.DeleteAllPaymentsAsync(request);
        }

        public async Task<ListPaymentsResponse> ListPayments(ListPaymentsRequest request)
        {
            return await LightningClient.ListPaymentsAsync(request);
        }
        public async Task<HTLCAttempt> SendPaymentViaRoute(Route route, Google.Protobuf.ByteString paymentHash = null)
        {
            var sha256 = SHA256.Create();

            if (paymentHash == null)
            {
                if (route.Hops.Last().CustomRecords.ContainsKey(5482373484L)) //Is Keysend
                {
                    var paymentId = route.Hops.Last().CustomRecords[5482373484L];
                    paymentHash = Google.Protobuf.ByteString.CopyFrom(sha256.ComputeHash(paymentId.ToByteArray()));
                }
                else
                {
                    var hash = sha256.ComputeHash("not-gonna-match".ToUtf8Bytes());
                    paymentHash = Google.Protobuf.ByteString.CopyFrom(hash);
                }
            }
            return await RouterClient.SendToRouteV2Async(
                new Routerrpc.SendToRouteRequest
                {
                    Route = route,
                    PaymentHash = paymentHash
                }, deadline: DateTime.UtcNow.AddMinutes(1));
        }
        public async Task<Payment> KeysendPayment(string dest, long amtSat, long feeLimitSat = 10, string message = null, int timeoutSeconds = 60)
        {
            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(randomBytes);
            var payment = new SendPaymentRequest
            {
                Dest = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString(dest)),
                Amt = amtSat,
                FeeLimitSat = feeLimitSat,
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
                TimeoutSeconds = timeoutSeconds,
            };
            payment.DestCustomRecords.Add(5482373484, Google.Protobuf.ByteString.CopyFrom(randomBytes));  //keysend
            if (message != null) 
                payment.DestCustomRecords.Add(34349334, Google.Protobuf.ByteString.CopyFrom(Encoding.Default.GetBytes(message))); //message type
            var streamingCallResponse = RouterClient.SendPaymentV2(payment);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
        }
        public async Task<ChannelGraph> DescribeGraph(bool includeUnannounced = false)
        {
            return await LightningClient.DescribeGraphAsync(new ChannelGraphRequest { IncludeUnannounced = includeUnannounced });
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
        public async Task<string> GetNodeAliasFromPubKey(string pubkey)
        {
            var node = await LightningClient.GetNodeInfoAsync(new NodeInfoRequest { PubKey = pubkey });
            return node.Node.Alias;
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
