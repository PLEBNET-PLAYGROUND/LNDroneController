﻿using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using ServiceStack.Text;
using ServiceStack;
using System.Runtime.InteropServices;
using Cln;

namespace LNDroneController.CLN
{
    public class CLNNodeConnection
    {
        public bool CertBugFixFlag {  get; set; }
        public GrpcChannel gRPCChannel { get; internal set; }
        public Node.NodeClient NodeClient { get; private set; }
        public string LocalNodePubKey { get; internal set; }
        public string LocalAlias { get; internal set; }
        public string ClearnetConnectString { get; internal set; }
        public string OnionConnectString { get; internal set; }

        //Start with manual startup
        public CLNNodeConnection()
        {
 
        }
        /// <summary>
        /// Constructor auto-start
        /// </summary>
        /// <param name="settings">LND Configuration Settings</param>
        public CLNNodeConnection(CLNSettings settings)
        {
            if (settings.ClientCertWithKey == null)
            {
                settings.ClientCertWithKey = X509Certificate2.CreateFromPem(
                    Encoding.UTF8.GetString(Convert.FromBase64String(settings.ClientCertBase64)),
                    Encoding.UTF8.GetString(Convert.FromBase64String(settings.ClientKeyBase64)));
            }
            CertBugFixFlag = settings.CertBugFixFlag; 
            Start(settings.ClientCertWithKey, settings.GrpcEndpoint);
        }

        /// <summary>
        /// Setup gRPC link
        /// </summary>
        /// <param name="cert"></param>
        /// <param name="host"></param>
        public void Start(X509Certificate2 cert, string host)
        {
            gRPCChannel = CreateGrpcConnection(host, cert);
            NodeClient = new Cln.Node.NodeClient(gRPCChannel);
            var nodeInfo = NodeClient.Getinfo(new Cln.GetinfoRequest { });
            LocalNodePubKey = Convert.ToHexString(nodeInfo.Id.ToByteArray());
            LocalAlias = nodeInfo.Alias;
            ClearnetConnectString = nodeInfo.Address.FirstOrDefault(x => !x.Address.Contains("onion")).Address;
            OnionConnectString = nodeInfo.Address.FirstOrDefault(x => x.Address.Contains("onion")).Address;
        }

        public void Dispose()
        {
            gRPCChannel.Dispose();
        }

        public GrpcChannel CreateGrpcConnection(string grpcEndpoint, X509Certificate2 cert)
        {
            // Due to updated ECDSA generated tls.cert we need to let gprc know that
            // we need to use that cipher suite otherwise there will be a handshake
            // error when we communicate with the lnd rpc server.


            //Windows bug fix work around.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || CertBugFixFlag)
            {
                var originalCert = cert;
                cert = new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
                originalCert.Dispose();
            }

            System.Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (httpRequestMessage, certificate, cetChain, policyErrors) =>
                {
                    return true;
                }
            };

            httpClientHandler.ClientCertificates.Add(cert);

            var grpcChannel = GrpcChannel.ForAddress(
                            grpcEndpoint,
                            new GrpcChannelOptions
                            {
                                DisposeHttpClient = true,
                                HttpHandler = httpClientHandler,
                                MaxReceiveMessageSize = 128000000,
                                MaxSendMessageSize = 128000000,
                            });
            return grpcChannel;
        }
    }
}
