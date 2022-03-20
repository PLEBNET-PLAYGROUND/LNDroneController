using Grpc.Core;
using Lnrpc;
using System;
using System.Threading.Tasks;
using Routerrpc;
using System.Diagnostics;
using ServiceStack.Text;

namespace LNDroneController.LND
{
    /// <summary>
    /// This is a very simple interceptor loop, it will block and requires response before moving to next HTLC event.
    /// Not for production use, but good for testing
    /// </summary>
    public class LNDSimpleHtlcInterceptorHandler
    {
        public LNDNodeConnection Node { get; }
        public event Func<ForwardHtlcInterceptRequest,Task<ForwardHtlcInterceptResponse>> OnIntercept;

        public LNDSimpleHtlcInterceptorHandler(LNDNodeConnection connection, Func<ForwardHtlcInterceptRequest, Task<ForwardHtlcInterceptResponse>> interceptLogic = null)
        {
            Node = connection;
            Task.Factory.StartNew(AttachInterceptor);
            if (interceptLogic == null)
            {
                OnIntercept = (data) =>
                {
                    Debug.Print(data.Dump());
                    return Task.FromResult(new ForwardHtlcInterceptResponse
                    {
                        Action = ResolveHoldForwardAction.Resume,
                        IncomingCircuitKey = data.IncomingCircuitKey,
                    });
                };
            }
            else
            {
                OnIntercept = interceptLogic;
            }
        }

        private async Task AttachInterceptor()
        {
            using (var streamingEvents = Node.RouterClient.HtlcInterceptor())
            {
                while (await streamingEvents.ResponseStream.MoveNext())
                {
                    var message = streamingEvents.ResponseStream.Current;
                    var result = OnIntercept(message);
                    await streamingEvents.RequestStream.WriteAsync(await result);
                }
            }
        }
    }
}