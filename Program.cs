using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Lnrpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Routerrpc;
using ServiceStack;
using ServiceStack.Text;
using LNDroneController.LND;
using System.Threading;
using System.Linq;
using LNDroneController.Types;
namespace LNDroneController
{
    class Program
    {
        private static Random r = new Random();
        async static Task Main(string[] args)
        {
            var nodeConnections = new List<LNDNodeConnection>();
            args = new string[3];
            for (int i = 0; i < 25; i++)
            {
                var node = new NodeConnectionSetings
                {
                    TlsCertFilePath = Environment.GetEnvironmentVariable("HOME") + $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/tls.cert".MapAbsolutePath(),
                    MacaroonFilePath = Environment.GetEnvironmentVariable("HOME") + $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/data/chain/bitcoin/signet/admin.macaroon".MapAbsolutePath(),
                    LocalIP = (Environment.GetEnvironmentVariable("HOME") + $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip".MapAbsolutePath()).ReadAllText().Replace("\n", string.Empty),
                    Host = $"playground-lnd-{i}:10009",
                };
                var nodeConnection = new LNDNodeConnection();
                nodeConnections.Add(nodeConnection);
                nodeConnection.Start(node.TlsCertFilePath, node.MacaroonFilePath, node.Host, node.LocalIP);
            }
            //03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083 -Ngu
            //023867414ef577da1ffd10364945f5023c4633c4a7a7f60b72898867df5ee02dda - tester
                try 
                {
                    var response = await nodeConnections[0].SendMessageV2("023867414ef577da1ffd10364945f5023c4633c4a7a7f60b72898867df5ee02dda", "Hello World!");
                    response.PrintDump();
                    response = await nodeConnections[0].SendMessageV2("03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083", "Hello World!");
                    response.PrintDump();
                }
                catch(Exception e)
                {
                    e.PrintDump();
                }
            foreach (var baseNode in nodeConnections)
            {
                
                // var nodes = await GetNewRandomNodes(nodeConnections, baseNode, 1);
                // Console.WriteLine($"Node: {baseNode.LocalAlias} : {baseNode.ClearnetConnectString}");

                // foreach (var connectToNode in nodes)
                // {
                //     try
                //     {
                //         Console.WriteLine($"Connecting: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                //         var result = await baseNode.Connect(connectToNode.ClearnetConnectString);
                //         result.PrintDump();
                //     }
                //     catch (Grpc.Core.RpcException e)
                //     {
                //         e.Status.Detail.PrintDump();
                //     }
                //     // try
                //     // {
                //     //     Console.WriteLine($"Opening: {connectToNode.LocalAlias}  10MSat");
                //     //     var result2 = await baseNode.OpenChannel(connectToNode.LocalNodePubKey,10000000L);
                //     //     result2.PrintDump();
                //     // }
                //     // catch (Grpc.Core.RpcException e)
                //     // {
                //     //     e.Status.Detail.PrintDump();
                //     // }
                // }
            }

            Console.WriteLine("Press ANY key to stop process");
            Console.ReadKey();

        }

        static async Task<List<LNDNodeConnection>> GetNewRandomNodes(List<LNDNodeConnection> nodes, LNDNodeConnection baseNode, int count)
        {
            var response = new List<LNDNodeConnection>();
            var randomMax = nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var existingChannels = await baseNode.GetChannels();

                var found = false;
                while (!found)
                {
                    var nextRandomNode = nodes[r.Next(randomMax)];
                    //not in existing list, not self, and not any existing channel
                    if (!response.Contains(nextRandomNode) &&  
                        nextRandomNode != baseNode &&
                        !existingChannels.Any(x=>x.RemotePubkey == nextRandomNode.LocalNodePubKey) )
                    {
                        found = true;
                        response.Add(nextRandomNode);
                    }
                }
            }
            return response;
        }

    }

}
