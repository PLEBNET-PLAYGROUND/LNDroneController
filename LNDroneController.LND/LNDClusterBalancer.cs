using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using ServiceStack;
using ServiceStack.Text;
using Dasync.Collections;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
namespace LNDroneController.LND
{
    public static class LNDClusterBalancer
    {
        private static Random r = new Random();
        public static List<LNDNodeConnection> ClusterNodes { get; set; }

        public static async Task Start(List<LNDNodeConnection> connections, CancellationToken token = default(CancellationToken))
        {
            ClusterNodes = connections;
            while (true)
            {
                if (token.IsCancellationRequested)
                    break;
                foreach (var node in ClusterNodes)
                {
                    //await node.TryReconnect();
                    var amount = 1_000_000L;
                    var channels = await node.ListActiveChannels();
                    var set = channels.FindRebalanceChannelSet(amount);
                    
                        // foreach(var target in set.remoteTargets)
                        // {
                        //    var result = await node.Rebalance(set.localSources.ToList(),target,amount);
                        //    var toNodeAlias = await node.GetNodeAliasFromPubKey(target.RemotePubkey);
                        //    $"{result.Status} = {node.LocalAlias} - {set.localSources.Select(x=>x.ChanId).ToJson()} to {toNodeAlias}:{target.ChanId} Local Balance: {target.LocalBalance/(double)1000000}/{target.Capacity/(double)1000000}MSat {result.Htlcs.LastOrDefault()?.Status}".Print();

                        // }
                        await set.remoteTargets.ParallelForEachAsync(async target => {
                            var result = await node.Rebalance(set.localSources.ToList(),target,amount);
                         // $"{node.LocalAlias} -d {set.localSources.Select(x=>x.ChanId).ToJson()} to {target.ChanId} Local Balance: {target.LocalBalance/(double)1000000}/{target.Capacity/(double)1000000}MSat {result.Htlcs.LastOrDefault()?.Status}".Print();
                          $"{node.LocalAlias} - {target.ChanId} Local Balance: {target.LocalBalance/(double)1000000}/{target.Capacity/(double)1000000}MSat {result.Htlcs.LastOrDefault()?.Status} in {result.Htlcs.LastOrDefault()?.Route.Hops.Count()} hops".Print();
                        }, 10);
                
                }
                Debug.Print("LNClusterRebalancer waiting for next loop...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
            return;
        }



    }
}