using System.Threading.Tasks;
using System.Threading;

namespace LNDroneController.LND
{
    public static class LNDAutoPaymentEngine
    {
        public static async Task Start(LNDNodeConnection connection, CancellationToken token)
        {
            while(true)
            {
                if (token.IsCancellationRequested)
                    break;

                
                await Task.Delay(1000);
            }
            return;
        }


    }
}