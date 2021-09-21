using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using LNDroneController.LND;
using Lnrpc;
using ServiceStack;
using ServiceStack.Text;

namespace LNDroneController.Extensions
{
    public static class LNDExtension
    {
        private static Random r = new Random();
        public static async Task<List<LNDNodeConnection>> GetNewRandomNodes(this List<LNDNodeConnection> nodes, LNDNodeConnection baseNode, int count, int maxCycleCount = 1000)
        {
            var response = new List<LNDNodeConnection>();
            var randomMax = nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var existingChannels = await baseNode.GetChannels();
                var found = false;
                var cycleCount = 0;
                while (!found)
                {
                    cycleCount++;
                    var nextRandomNode = nodes[r.Next(randomMax)];
                    //find nodes not in existing list, not self, and not any existing channel
                    if (!response.Contains(nextRandomNode) &&
                        nextRandomNode != baseNode &&
                        !existingChannels.Any(x => x.RemotePubkey == nextRandomNode.LocalNodePubKey))
                    {
                        found = true;
                        response.Add(nextRandomNode);
                    }
                    if (cycleCount >= maxCycleCount)
                        break;
                }
            }
            return response;
        }
        public static async Task<List<LightningNode>> GetNewRandomNodes(this RepeatedField<LightningNode> nodes, LNDNodeConnection baseNode, int count, int maxCycleCount = 1000)
        {
            var response = new List<LightningNode>();
            var randomMax = nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var existingChannels = await baseNode.GetChannels();
                var found = false;
                var cycleCount = 0;
                while (!found)
                {
                    cycleCount++;
                    var nextRandomNode = nodes[r.Next(randomMax)];
                    //find nodes not in existing list, not self, and not any existing channel
                    if (!response.Contains(nextRandomNode) &&
                        nextRandomNode.PubKey != baseNode.LocalNodePubKey &&
                        !existingChannels.Any(x => x.RemotePubkey == nextRandomNode.PubKey))
                    {
                        found = true;
                        response.Add(nextRandomNode);
                    }
                    if (cycleCount >= maxCycleCount)
                        break;
                }
            }
            return response;
        }
        public static LightningNode ToLightningNode(this LNDNodeConnection node)
        {
            return new LightningNode
            {
                Alias = node.LocalAlias,
                PubKey = node.LocalNodePubKey
            };
        }
        public static List<LightningNode> ToLightningNodes(this List<LNDNodeConnection> nodes)
        {
            return nodes.ConvertAll(x => x.ToLightningNode());
        }
    }
}

