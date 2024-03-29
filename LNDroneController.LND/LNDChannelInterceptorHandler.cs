using Grpc.Core;
using Lnrpc;
using Routerrpc;
using System;
using System.Threading.Tasks;

namespace LNDroneController.LND
{

    public class LNDChannelInterceptorHandler
    {
        public LNDNodeConnection Node { get; }
        public event Func<ChannelAcceptRequest, Task<ChannelAcceptResponse>> OnChannelRequest;


        public LNDChannelInterceptorHandler(LNDNodeConnection connection)
        {
            Node = connection;
            Task.Factory.StartNew(ListenForCustomMessages);
        }

        private async Task ListenForCustomMessages()
        {
            using (var streamingEvents = Node.LightningClient.ChannelAcceptor())
            {
                while (await streamingEvents.ResponseStream.MoveNext())
                {
                    var channelRequest = streamingEvents.ResponseStream.Current;
                    var response = OnChannelRequest(channelRequest);
                    await streamingEvents.RequestStream.WriteAsync(await response);
                }
            }
        }


    }

}