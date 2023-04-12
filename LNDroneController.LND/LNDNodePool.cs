using NBitcoin.Logging;
using ServiceStack;
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
        private readonly List<LNDNodeConnection> Nodes = new List<LNDNodeConnection>();
        public readonly List<LNDNodeConnection> ReadyNodes = new List<LNDNodeConnection>();
        
        private readonly List<LNDSettings> LNDNodesNotYetInitialized = new List<LNDSettings>();

        private PeriodicTimer RPCCheckTimer;
        private readonly TimeSpan UpdateReadyStatesPeriod = TimeSpan.FromSeconds(5);
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

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
                LNDNodesNotYetInitialized.Add(node);
            }
        }

        private void SetupNotYetInitializedNodes()
        {
            var lndNodes = LNDNodesNotYetInitialized.CreateCopy();

            foreach (var settings in lndNodes)
            {
                try
                {
                    var node = new LNDNodeConnection(settings);
                    Nodes.Add(node);
                    LNDNodesNotYetInitialized.Remove(LNDNodesNotYetInitialized.First(x => x.GrpcEndpoint == settings.GrpcEndpoint));
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
        }
        private void SetupTimers()
        {
            RPCCheckTimer = new PeriodicTimer(UpdateReadyStatesPeriod);
            Task.Run(async () => await UpdateReadyStates(), _cancellationTokenSource.Token);
        }

        private async Task UpdateReadyStates() //TIMER
        {
            while (await RPCCheckTimer.WaitForNextTickAsync(_cancellationTokenSource.Token))
            {
                SetupNotYetInitializedNodes();
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
            foreach (var node in Nodes)
            {
                node.Dispose();
            }
        }
    }
}
