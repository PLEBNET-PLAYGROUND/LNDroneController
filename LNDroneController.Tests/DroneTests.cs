using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using LNDroneController;
using LNDroneController.Types;
using LNDroneController.LND;
using ServiceStack;
using ServiceStack.Text;
using Lnrpc;
using NuGet.Frameworks;

namespace LNDroneController.Tests
{
    public class DroneTests
    {
        private List<LNDNodeConnection> NodeConnections = new List<LNDNodeConnection>();

        [SetUp]
        public void Setup()
        {
            for (var i = 0; i < 25; i++)
            {
                var node = new NodeConnectionSettings
                {
                    TlsCertFilePath = Environment.GetEnvironmentVariable("HOME") +
                                      $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/tls.cert".MapAbsolutePath(),
                    MacaroonFilePath = Environment.GetEnvironmentVariable("HOME") +
                                       $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/data/chain/bitcoin/signet/admin.macaroon"
                                           .MapAbsolutePath(),
                    LocalIP = (Environment.GetEnvironmentVariable("HOME") +
                               $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip".MapAbsolutePath())
                        .ReadAllText().Replace("\n", string.Empty),
                    Host = $"playground-lnd-{i}:10009",
                };
                var nodeConnection = new LNDNodeConnection();
                NodeConnections.Add(nodeConnection);
                nodeConnection.StartWithFilePaths(node.TlsCertFilePath, node.MacaroonFilePath, node.Host, node.LocalIP);
            }
        }

        [Test]
        public async Task GetBalances()
        {
            foreach (var i in Enumerable.Range(0, 24))
            {
                var result = await NodeConnections[i].GetChannels();
                $"Node # {i}".Print();
                foreach(var r in result)
                {
                    $"{r.ChanId} - {r.LocalBalance/(double)r.Capacity}".Print();
                }
            }
            
        }

        [Test]
        public async Task ListPayments()
        {
            foreach (var node in NodeConnections)
            {
                var result = await NodeConnections[0].ListPayments(new ListPaymentsRequest
                {
                    IncludeIncomplete = false
                });
                $"{node.LocalAlias} - {result.Payments.Count}".Print(); 
            }
            
        }
        [Test]
        public async Task CheckUnableToLocateInvoice()
        {
            try
            {
                var checkStatus = await NodeConnections[0].CheckInvoiceStatus(new PaymentHash
                {
                    RHash = ByteString.Empty
                });
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Unknown && e.Status.Detail == "unable to locate invoice")
            {
                Assert.Pass();
            }
            Assert.Fail();
        }
        
        [Test]
        public async Task ListInvoices()
        {
            foreach (var node in NodeConnections)
            {
                var result = await NodeConnections[0].ListInvoices(new ListInvoiceRequest
                {
                    NumMaxInvoices = 1000000
                });
                var settledCount = result.Invoices.Where(t => t.State == Invoice.Types.InvoiceState.Settled).Count();
                $"{node.LocalAlias} - {settledCount} of {result.Invoices.Count}".Print(); 
            }
            
        }
        [Test]
        public async Task PurgePaymentsFailedHTLCs()
        {
            var result = await NodeConnections[0].PurgePayments(new DeleteAllPaymentsRequest{
                FailedHtlcsOnly = true,
            });
            result.PrintDump();

        }
        
      
        [Test]
        public async Task PurgeAllPayments()
        {
            var result = await NodeConnections[0].PurgePayments(new DeleteAllPaymentsRequest{
            });
            result.PrintDump();

        }
        [Test]
        public async Task PurgeFailedPayments()
        {
            var result = await NodeConnections[0].PurgePayments(new DeleteAllPaymentsRequest{
                FailedPaymentsOnly = true,
            });
            result.PrintDump();

        }
        [Test]
        public async Task TryReconnect()
        {
            foreach (var node in NodeConnections)
            {
                await node.TryReconnect();
            }
        }
        [Test]
        public async Task ListActiveChannels()
        {
            var channels = await NodeConnections[0].ListActiveChannels();
            channels.PrintDump();
        }

        [Test]
        public async Task ListPeers()
        {
            var peers = await NodeConnections[0].ListPeers();
            peers.PrintDump();
        }
        [Test]
        public async Task ListInactiveChannels()
        {
            await NodeConnections.ParallelForEachAsync( async node =>
            {
                var channels = await node.ListInactiveChannels();
                if (channels.Count > 0)
                {
                    $"{node.LocalAlias}:".Print();
                    channels.PrintDump();
                }
            },1);
        }
        [Test]
        public async Task ListAllChannels()
        {
            var channels = await NodeConnections[0].ListChannels(new ListChannelsRequest());
            channels.PrintDump();
        }
        [Test]
        public async Task GetInfo()
        {
            var bag = new ConcurrentBag<GetInfoResponse>();
            await NodeConnections.ParallelForEachAsync(async n =>
            {
                bag.Add(await n.GetInfo());
            }, 20);
            foreach (var x in bag)
            {
                x.Uris.PrintDump();
            }
        }
        [Test]
        public async Task DescribeGraph()
        {
            var graph = await NodeConnections[0].DescribeGraph();
            graph.PrintDump();
        }
        [Test]
        public async Task FindRandomNodes()
        {
            var graph = await NodeConnections[0].DescribeGraph();
            var nodes = await graph.Nodes.GetNewRandomNodes(NodeConnections[0], 10, 100);
            nodes.PrintDump();
        }
        [Test]
        public void ConvertToLightningNodes()
        {
            var nodes = NodeConnections.ToLightningNodes();
            nodes.PrintDump();
        }
        [Test]
        public async Task QueryRoutes()
        {
            var response = await NodeConnections[0].QueryRoutes("03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083", keySend: true);
            response.PrintDump();
        }

        [Test]
        public async Task ManualRoutePayment()
        {
            var routes = await NodeConnections[0].QueryRoutes("03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083", keySend: true);
            var paymentRes = await NodeConnections[0].SendPaymentViaRoute(routes[0]);
            paymentRes.PrintDump();
        }

    }
}