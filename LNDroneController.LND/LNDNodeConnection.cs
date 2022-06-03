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
using Signrpc;

namespace LNDroneController.LND
{
    public class LNDNodeConnection : IDisposable
    {
        private Random r = new Random();

        public GrpcChannel gRPCChannel { get; internal set; }
        public Lightning.LightningClient LightningClient { get; internal set; }
        public Router.RouterClient RouterClient { get; internal set; }
        public Signer.SignerClient SignClient { get; internal set; }
        public State.StateClient StateClient { get; internal set; }
        public string LocalNodePubKey { get; internal set; }
        public string LocalAlias { get; internal set; }
        public string ClearnetConnectString { get; internal set; }
        public string OnionConnectString { get; internal set; }

        public bool IsReady
        {
            get
            {
                return CheckRPCReady();
            }
        }

        //Start with manual startup
        public LNDNodeConnection()
        {

        }
        /// <summary>
        /// Constructor auto-start
        /// </summary>
        /// <param name="settings">LND Configuration Settings</param>
        public LNDNodeConnection(LNDSettings settings)
        {
            if (settings.MacaroonPath.IsNullOrEmpty())
            {
                StartWithBase64(settings.TLSCertBase64, settings.MacaroonBase64, settings.GrpcEndpoint, settings.LocalIP);
            }
            else
            {
                StartWithFilePaths(settings.TLSCertPath, settings.MacaroonPath, settings.GrpcEndpoint, settings.LocalIP);
            }
        }
        public void StartWithFilePaths(string tlsCertFilePath, string macoroonFilePath, string host, string localIP = null)
        {
            byte[] macaroonBytes = System.IO.File.ReadAllBytes(macoroonFilePath);
            var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", ""); // hex format stripped of "-" chars
            var cert = System.IO.File.ReadAllText(tlsCertFilePath);
            StartWithBase64(Convert.ToBase64String(cert.ToAsciiBytes()), Convert.ToBase64String(Convert.FromHexString(macaroon)), host, localIP);
        }

        public void StartWithBase64(string tlsCertBase64, string macaroonBase64, string host, string localIP = null)
        {
            gRPCChannel = CreateGrpcConnection(host, tlsCertBase64, macaroonBase64);
            LightningClient = new Lnrpc.Lightning.LightningClient(gRPCChannel);
            RouterClient = new Routerrpc.Router.RouterClient(gRPCChannel);
            SignClient = new Signrpc.Signer.SignerClient(gRPCChannel);
            StateClient = new State.StateClient(gRPCChannel);
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

        public void Dispose()
        {
            gRPCChannel.Dispose();
        }

        public GrpcChannel CreateGrpcConnection(string grpcEndpoint, string TLSCertBase64, string MacaroonBase64)
        {
            // Due to updated ECDSA generated tls.cert we need to let gprc know that
            // we need to use that cipher suite otherwise there will be a handshake
            // error when we communicate with the lnd rpc server.

            System.Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
            };
            var x509Cert = new X509Certificate2(Convert.FromBase64String(TLSCertBase64));

            httpClientHandler.ClientCertificates.Add(x509Cert);
            string macaroon;

            macaroon = Convert.FromBase64String(MacaroonBase64).ToHex();


            var credentials = CallCredentials.FromInterceptor((_, metadata) =>
            {
                metadata.Add(new Metadata.Entry("macaroon", macaroon));
                return Task.CompletedTask;
            });

            var grpcChannel = GrpcChannel.ForAddress(
                            grpcEndpoint,
                            new GrpcChannelOptions
                            {
                                DisposeHttpClient = true,
                                HttpHandler = httpClientHandler,
                                Credentials = ChannelCredentials.Create(new SslCredentials(), credentials),
                                MaxReceiveMessageSize = 128000000,
                                MaxSendMessageSize = 128000000,
                            });
            return grpcChannel;
        }

        // public async Task<NodeInfo> GetNodeInfo(string remotePubkey, bool includeChannels = false )
        // {
        //     return await LightningClient.GetNodeInfoAsync(new NodeInfoRequest{
        //         PubKey = remotePubkey,
        //         IncludeChannels = includeChannels,
        //     });
        // }

        public async Task<ChannelEdge> GetChannelInfo(ulong chanId)
        {
            return await LightningClient.GetChanInfoAsync(new ChanInfoRequest { ChanId = chanId });
        }

        public async Task<SharedKeyResponse> DeriveSharedKey(string ephemeralPubkey)
        {
            return await SignClient.DeriveSharedKeyAsync(new SharedKeyRequest
            {
                EphemeralPubkey = ByteString.CopyFrom(Convert.FromHexString(ephemeralPubkey))
            });
        }

        public bool CheckRPCReady()
        {
            try
            {
                var res = StateClient.GetState(new GetStateRequest());
                return res.State == WalletState.RpcActive;
            }
            catch (RpcException e)
            {
            }
            catch (Exception e)
            {
            }
            return false;
        }

        public async Task<Payment> Rebalance(IList<Channel> sources, Channel target, long amount, int timeoutSeconds = 30, bool isAmp = false)
        {
            //    var paymentReq = await LightningClient.AddInvoiceAsync(new Invoice{ Value = amount, Expiry = 60, Memo = "Rebalance..."});

            var randomBytes = new byte[32];
            r.NextBytes(randomBytes);
            var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(randomBytes);
            var req = new SendPaymentRequest
            {
                Amp = isAmp,
                AllowSelfPayment = true,
                Amt = amount,
                LastHopPubkey = ByteString.CopyFrom(Convert.FromHexString(target.RemotePubkey)),
                FeeLimitSat = (long)(amount * (1 / 200.0)) + 10,
                TimeoutSeconds = timeoutSeconds,
                NoInflightUpdates = true,
                Dest = ByteString.CopyFrom(Convert.FromHexString(LocalNodePubKey)), //self
                PaymentHash = ByteString.CopyFrom(hash),
            };
            foreach (var chan in sources)
            {
                req.OutgoingChanIds.Add(chan.ChanId);
            }
            req.DestCustomRecords.Add(5482373484, ByteString.CopyFrom(randomBytes));  //keysend 

            var streamingCallResponse = RouterClient.SendPaymentV2(req);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
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
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest { ActiveOnly = true });
            return response.Channels.ToList();
        }
        /// <summary>
        /// List all inactive channels
        /// </summary>
        public async Task<List<Channel>> ListInactiveChannels()
        {
            var response = await LightningClient.ListChannelsAsync(new ListChannelsRequest { InactiveOnly = true });
            return response.Channels.ToList();
        }

        public async Task<ListPeersResponse> ListPeers()
        {
            return await LightningClient.ListPeersAsync(new ListPeersRequest { LatestError = true });
        }

        public async Task TryReconnect()
        {
            var inactive = await ListInactiveChannels();
            foreach (var chan in inactive)
            {
                var nodeInfo = await GetNodeInfo(chan.RemotePubkey, false);
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
                    catch (RpcException e) when (e.StatusCode == StatusCode.Unknown)
                    {
                        Debug.Print(e.Dump());
                    }

                }
            }

            return;
        }

        public async Task<NodeInfo> GetNodeInfo(string pubkey, bool includeChannels = false)
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
            return await LightningClient.DisconnectPeerAsync(new DisconnectPeerRequest { PubKey = pubkey });
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

        public async Task<PolicyUpdateResponse> UpdateChannelPolicy(PolicyUpdateRequest policy)
        {
            return await LightningClient.UpdateChannelPolicyAsync(policy);
        }
        public async Task<Payment> KeysendPayment(string dest, long amtSat, long feeLimitSat = 10, string message = null, int timeoutSeconds = 60, Dictionary<ulong, byte[]> keySendPairs = null)
        {
            
            var randomBytes = RandomNumberGenerator.GetBytes(32); // new byte[32];
            //r.NextBytes(randomBytes);
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
            if (keySendPairs != null)
            {
                foreach (var kvp in keySendPairs)
                {
                    payment.DestCustomRecords.Add(kvp.Key, ByteString.CopyFrom(kvp.Value));
                }
            }
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


        public async Task<ListInvoiceResponse> ListInvoices(ListInvoiceRequest request)
        {
            return await LightningClient.ListInvoicesAsync(request);
        }

        public async Task<AddInvoiceResponse> GenerateInvoice(Invoice invoice)
        {
            return await LightningClient.AddInvoiceAsync(invoice);
        }

        public async Task<Payment> PayPaymentRequest(string invoicePaymentRequest, long maxFeeRateSats = 10, int timeOutSeconds = 20)
        {
            var payment = new SendPaymentRequest
            {
                PaymentRequest = invoicePaymentRequest,
                FeeLimitSat = maxFeeRateSats,
                TimeoutSeconds = timeOutSeconds,
            };
            var streamingCallResponse = RouterClient.SendPaymentV2(payment);
            Payment paymentResponse = null;
            await foreach (var res in streamingCallResponse.ResponseStream.ReadAllAsync())
            {
                paymentResponse = res;
            }
            return paymentResponse;
        }

        public async Task<Invoice> CheckInvoiceStatus(PaymentHash request)
        {
            return await LightningClient.LookupInvoiceAsync(request);
        }
    }
}
