using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
    public class Tests
    {
        //03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083 - Ngu
        //023867414ef577da1ffd10364945f5023c4633c4a7a7f60b72898867df5ee02dda - testing tester
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
        public async Task TryReconnect()
        {
            await NodeConnections[0].TryReconnect();
        }

        [Test]
        public async Task CrossConnectCluster()
        {
            
                foreach (var baseNode in NodeConnections)
                {
                    foreach (var node in NodeConnections)
                    {
                        if (node.LocalNodePubKey != baseNode.LocalNodePubKey)
                        {
                            try
                            {
                                var result = await baseNode.Connect(node.ClearnetConnectString);
                                result.PrintDump();
                            }
                            catch (RpcException e) when (e.StatusCode == StatusCode.Unknown)
                            {
                                e.PrintDump();
                            }
                        }
                    }
                }
            
        }
        
        [Test]
        public void CrossPayCluster()
        {
            var tasks = new List<Task<Payment>>(); 
                foreach (var baseNode in NodeConnections.GetRandomFromList(5,100))
                {
                    foreach (var node in NodeConnections)
                    {
                        if (node.LocalNodePubKey != baseNode.LocalNodePubKey)
                        {
                             tasks.Add(baseNode.KeysendPayment(node.LocalNodePubKey, 100, timeoutSeconds: 5));
                        }
                    }
                }

                Task.WaitAll(tasks.ToArray());
                var good = tasks.Where(x => x.Result.Status == Payment.Types.PaymentStatus.Succeeded).Count();
                var bad = tasks.Where(x => x.Result.Status == Payment.Types.PaymentStatus.Failed).Count();
                $"Success: {good} Fail: {bad} % success: {(1.0*good/(bad+good))}".Print();
                
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
        public async Task ConnectRandomNodes()
        {
            foreach (var baseNode in NodeConnections)
            {

                var nodes = await NodeConnections.GetNewRandomNodes(baseNode, 15);

                Console.WriteLine($"Node: {baseNode.LocalAlias}");

                foreach (var connectToNode in nodes)
                {
                    try
                    {
                        Console.WriteLine($"Connecting: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                        var result = await baseNode.Connect(connectToNode.ClearnetConnectString);
                        result.PrintDump();
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        e.Status.Detail.PrintDump();
                    }
                }
            }
        }


        [Test]
        [Ignore("Remove Ignore if you really want to do this")]
        public async Task OpenChannelsWithRandomNodes()
        {
            foreach (var baseNode in NodeConnections)
            {

                var nodes = await NodeConnections.GetNewRandomNodes(baseNode, 15);

                Console.WriteLine($"Node: {baseNode.LocalAlias}");

                foreach (var connectToNode in nodes)
                {
                    try
                    {
                        Console.WriteLine($"Connecting: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                        var result = await baseNode.Connect(connectToNode.ClearnetConnectString);
                        result.PrintDump();
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        e.Status.Detail.PrintDump();
                    }
                    try
                    {
                        Console.WriteLine($"Opening: {connectToNode.LocalAlias}  10MSat");
                        var result2 = await baseNode.OpenChannel(connectToNode.LocalNodePubKey, 10000000L);
                        result2.PrintDump();
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        e.Status.Detail.PrintDump();
                    }
                }
            }
        }

        [Test]
        public async Task QueryRoutes()
        {
            var response = await NodeConnections[0].QueryRoutes("03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083", keySend:true);
            response.PrintDump();
        }

        [Test]
        public async Task ManualRoutePayment()
        {
            var routes = await NodeConnections[0].QueryRoutes("03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083",keySend:true);
            var paymentRes = await NodeConnections[0].SendPaymentViaRoute(routes[0]);
            paymentRes.PrintDump();
        }
        [Test]
        public async Task ProbePayment()
        {
            var response = await NodeConnections[0].ProbePayment("03ee9d906caa8e8e66fe97d7a76c2bd9806813b0b0f1cee8b9d03904b538f53c4e", 10);
            var result = response.FailureReason.ToString() == "FailureReasonIncorrectPaymentDetails" ? "Online" : response.FailureReason.ToString();
            $"HelloJessica [signet] @ 10sats - {result} - {response.FailureReason}".Print();
        }
        [Test]
        public async Task SendPaymentWithMessage()
        {
            var response = await NodeConnections[0].KeysendPayment("03ee9d906caa8e8e66fe97d7a76c2bd9806813b0b0f1cee8b9d03904b538f53c4e", 10, 10, message: "Hello World!");
            response.PrintDump();
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

        private static async Task KeySendWithMessage(LNDNodeConnection baseNode, LNDNodeConnection connectToNode)
        {
            try
            {
                Console.WriteLine($"KeySending: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                var result = await baseNode.KeysendPayment(connectToNode.LocalNodePubKey, 10, 10, "Hello World!");
                int i = 0;
                foreach (var h in result.Htlcs)
                {
                    i++;
                    $"HTLC Attempt #{i} : Hops {h.Route.Hops.Count} Status:{h.Status}".Print();
                }
            }
            catch (Grpc.Core.RpcException e)
            {
                e.Status.Detail.PrintDump();
                Assert.Fail();
            }
        }
        private static async Task KeySendWithMessage(LNDNodeConnection baseNode, LightningNode connectToNode)
        {
            try
            {
                Console.WriteLine($"KeySending: {connectToNode.Alias} : {connectToNode.PubKey}");
                var result = await baseNode.KeysendPayment(connectToNode.PubKey, 10, 10, "Hello World!");
                int i = 0;
                foreach (var h in result.Htlcs)
                {
                    i++;
                    $"HTLC Attempt #{i} : Hops {h.Route.Hops.Count} Status:{h.Status}".Print();
                }
            }
            catch (Grpc.Core.RpcException e)
            {
                e.Status.Detail.PrintDump();
                Assert.Fail();
            }
        }
    }
}