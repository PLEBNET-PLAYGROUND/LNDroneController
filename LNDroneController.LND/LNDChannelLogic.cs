using Grpc.Core;
using Lnrpc;
using Routerrpc;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using ServiceStack;
using System.Security.Cryptography;
using Grpc.Net.Client;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Security.Policy;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace LNDroneController.LND
{
    public static class LNDChannelLogic
    {
        public static double MinLocalBalancePercentage { get; set; } = 0.2;
        public static double MaxRemoteLocalBalancePercentage { get; set; } = 0.2;

        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> source)
        {
            Random rnd = new Random();
            return source.OrderBy((item) => rnd.Next());
        }

    }
}