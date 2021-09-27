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
// using Serilog;
namespace LNDroneController.LND
{
    public static class LNDAutoPaymentEngine 
    {
        private static Random r = new Random();
        public static List<LNDNodeConnection> ClusterNodes {get; set;}

        public static async Task Start(LNDNodeConnection connection, TimeSpan delayBetweenPayments, int numberOfPayments = 1, int minSendAmount = 1, int maxSendAmount = 100000, CancellationToken token = default(CancellationToken))
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                    break;
                var graph = await connection.DescribeGraph();

                var amount = r.Next(minSendAmount, maxSendAmount);
                var nodesToPay = await ClusterNodes.GetNewRandomNodes(connection, numberOfPayments);

                await nodesToPay.ParallelForEachAsync(async n =>
                {
                    try
                    {
                        var payment = await connection.KeysendPayment(n.LocalNodePubKey, amount, (long)(10+ amount * (1 / 500.0)), null, 20); //2000 ppm max fee, 20second timeout
                        if (payment.FailureReason == Lnrpc.PaymentFailureReason.FailureReasonNone)
                        {
                            $"{DateTime.UtcNow} - {connection.LocalAlias} - {amount} sats sent at {Math.Ceiling(payment.FeeSat/(decimal)amount*1000000)} ppm rate in {payment.Htlcs.Last(x=>x.Status == Lnrpc.HTLCAttempt.Types.HTLCStatus.Succeeded).Route.Hops.Count} hops to {n.LocalAlias} ({n.LocalNodePubKey})".Print();
                        }
                        else
                        {
                            var chanId = payment.Htlcs.LastOrDefault()?.Route.Hops.LastOrDefault()?.ChanId;
                            if (chanId.HasValue)
                            {
                                $"{DateTime.UtcNow} - {connection.LocalAlias} - {amount} sat failed to send: {payment.FailureReason} to {n.LocalAlias}".Print();
                            }
                            else
                            {
                                
                                $"{DateTime.UtcNow} - {connection.LocalAlias} - {amount} sat failed to send: {n.LocalAlias} - {payment.Status}".Print();
                            }
                           
                        }
                    }
                    catch (Exception e)
                    {
                        $"{DateTime.UtcNow} - {e}".Print();
                        //suck up errors
                    }
                }, 4, token);

                await Task.Delay(delayBetweenPayments);
            }
            return;
        }



    }
}