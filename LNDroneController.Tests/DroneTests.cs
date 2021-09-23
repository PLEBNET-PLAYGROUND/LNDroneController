using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Grpc.Core;
using NUnit.Framework;
using LNDroneController;
using LNDroneController.Types;
using LNDroneController.LND;
using ServiceStack;
using ServiceStack.Text;
using Lnrpc;
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
                var node = new NodeConnectionSetings
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
                nodeConnection.Start(node.TlsCertFilePath, node.MacaroonFilePath, node.Host, node.LocalIP);
            }
        }
        [Test]
        public async Task ListPayments()
        {
            var result = await NodeConnections[0].ListPayments(new ListPaymentsRequest
            {
                IncludeIncomplete = false
            });
            result.Payments.Count.Print();
            result.PrintDump();
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
            await NodeConnections[0].TryReconnect();
        }
        [Test]
        public async Task ListActiveChannels()
        {
            var channels = await NodeConnections[0].ListActiveChannels();
            channels.PrintDump();
        }
        [Test]
        public async Task ListInactiveChannels()
        {
            var channels = await NodeConnections[0].ListInactiveChannels();
            channels.PrintDump();
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