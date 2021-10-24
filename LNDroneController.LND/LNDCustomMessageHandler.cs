using Grpc.Core;
using Lnrpc;
using System;
using System.Threading.Tasks;

namespace LNDroneController.LND
{

    public class LNDCustomMessageHandler
    {
        public LNDNodeConnection Node { get; }
        public event EventHandler<CustomMessage> OnMessage;

        public LNDCustomMessageHandler(LNDNodeConnection connection)
        {
            Node = connection;
            Task.Factory.StartNew(ListenForCustomMessages);
        }

        public async Task<SendCustomMessageResponse> SendCustomMessageRequest(CustomMessage m)
        {
            return await Node.LightningClient.SendCustomMessageAsync(new SendCustomMessageRequest
            {
                Data = m.Data,
                Peer = m.Peer,
                Type = m.Type,
            });
        }

        private async Task ListenForCustomMessages()
        {
            using (var streamingEvents = Node.LightningClient.SubscribeCustomMessages(new Lnrpc.SubscribeCustomMessagesRequest()))
            {
                while(await streamingEvents.ResponseStream.MoveNext())
                {
                    var message = streamingEvents.ResponseStream.Current;
                    OnMessage(Node, message);
                }
            }
        }


    }

}