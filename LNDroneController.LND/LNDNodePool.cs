using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LNDroneController.LND
{
    public interface ILNDNodePool
    {
        void Dispose();
        LNDNodeConnection GetLNDNodeConnection();
        LNDNodeConnection GetLNDNodeConnection(string pubkey);
    }

    public class LNDNodePool : IDisposable, ILNDNodePool
    {
        private List<LNDNodeConnection> Nodes;
        private List<LNDNodeConnection> ReadyNodes;

        private Timer RPCCheckTimer;
        private readonly TimeSpan UpdateReadyStatesPeriod = TimeSpan.FromSeconds(5);

        public LNDNodePool(List<LNDNodeConnection> nodes)
        {
            Nodes = nodes;
            SetupTimers();
        }
        public LNDNodePool(List<LNDSettings> nodeSettings)
        {
            BuildConnections(nodeSettings);
            SetupTimers();
        }

        private void BuildConnections(List<LNDSettings> nodeSettings)
        {
            foreach (var node in nodeSettings)
            {
                Nodes.Add(new LNDNodeConnection(node));

            }
        }
        private void SetupTimers()
        {
            RPCCheckTimer = new Timer(UpdateReadyStates, null, UpdateReadyStatesPeriod, UpdateReadyStatesPeriod);
        }

        private void UpdateReadyStates(object state) //TIMER
        {
            foreach (var node in Nodes)
            {
                if (!node.IsReady)
                {
                    ReadyNodes.Remove(node);
                }
                else
                {
                    if (!ReadyNodes.Contains(node))
                    {
                        ReadyNodes.Add(node);
                    }
                }
            }
        }

        /// <summary>
        /// Gets next free LNDNode based on logic
        /// </summary>
        /// <returns></returns>
        public LNDNodeConnection GetLNDNodeConnection()
        {
            return ReadyNodes.First();
        }

        /// <summary>
        /// Gets specific node based on pubkey, if not found returns null
        /// </summary>
        /// <param name="pubkey"></param>
        /// <returns></returns>
        public LNDNodeConnection GetLNDNodeConnection(string pubkey)
        {
            foreach (var node in Nodes)
            {
                if (node.LocalNodePubKey == pubkey)
                    return node;
            }
            return null;
        }

        public void Dispose()
        {
            foreach(var node in Nodes)
            {
                node.Dispose();
            }
        }
    }
}
