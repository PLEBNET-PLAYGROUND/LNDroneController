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
        public static long MinLocalBalanceSats { get; set; } = 1000000L; 
        public static long MinRemoteBalanceSats { get; set; } = 1000000L;        
    }
}