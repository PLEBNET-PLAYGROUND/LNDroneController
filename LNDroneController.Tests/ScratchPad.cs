using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using LNDroneController;
using LNDroneController.Types;
using LNDroneController.LND;
using ServiceStack;
using ServiceStack.Text;
using Lnrpc;
using ServiceStack.Common;
using System.Security.Cryptography;
using LNDroneController.Extentions;
using NBitcoin;

namespace LNDroneController.Tests
{
    public class ScratchPad
    {
        //03c14f0b2a07a7b3eb2701bf03fafe65bc76c7c1aac77f7d57a9e9bb31a9107083 - Ngu
        //023867414ef577da1ffd10364945f5023c4633c4a7a7f60b72898867df5ee02dda - testing tester
        private List<LNDNodeConnection> NodeConnections = new List<LNDNodeConnection>();

        [SetUp]
        public void Setup()
        {
            for (var i = 0; i < 25; i++)
            {
                var node = new NodeConnectionSettings
                {
                    TlsCertFilePath = Environment.GetEnvironmentVariable("HOME") +
                                      $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/tls.cert".MapAbsolutePath(),
                    MacaroonFilePath = Environment.GetEnvironmentVariable("HOME") +
                                       $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/data/chain/bitcoin/signet/admin.macaroon"
                                           .MapAbsolutePath(),
                    LocalIPPath = (Environment.GetEnvironmentVariable("HOME") +
                               $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip".MapAbsolutePath()),
                    LocalIP = (Environment.GetEnvironmentVariable("HOME") +
                               $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip".MapAbsolutePath())
                        .ReadAllText().Replace("\n", string.Empty),
                    Host = $"https://playground-lnd-{i}:10009",
                };
                var nodeConnection = new LNDNodeConnection();
                NodeConnections.Add(nodeConnection);
                nodeConnection.StartWithFilePaths(node.TlsCertFilePath, node.MacaroonFilePath, node.Host, node.LocalIP);
            }
        }

        [Ignore("BTC RCP Tests")]
        [Test]
        public async Task BTCRPC()
        {
            var nc = new System.Net.NetworkCredential("bitcoin", "bitcoin");
            var rpc = new NBitcoin.RPC.RPCClient(nc, "http://54.175.33.10:38332", NBitcoin.Bitcoin.Instance.Signet);
            var result = rpc.GetBlockchainInfo();
            result.PrintDump();
            var address = rpc.GetNewAddress(new NBitcoin.RPC.GetNewAddressRequest { AddressType = NBitcoin.RPC.AddressType.Bech32 });
            var spend = rpc.SendToAddress(address, Money.Coins(0.01m),new NBitcoin.RPC.SendToAddressParameters {FeeRate= new FeeRate(10m) });
            var mempool = rpc.GetMemPool();
            spend.PrintDump();
        }

         

        [Test]
        public async Task UpdateChannelPolicy()
        {
             await NodeConnections.ParallelForEachAsync(async n =>
            {
                var r =await n.UpdateChannelPolicy(new PolicyUpdateRequest
                {
                    BaseFeeMsat = 420,
                    Global = true,
                    FeeRate = .000500,
                    TimeLockDelta = 40,
                    MinHtlcMsat = 1,
                });
                r.PrintDump();
            }, 10);
            
        }
            [Test]
        public void GenerateJSONConfigFile()
            {
                var saveTo = Environment.GetEnvironmentVariable("HOME") + "/LNDroneController/drone-config.json";
                var data = new List<NodeConnectionSettings>();
                for (var i = 0; i < 25; i++)
                {
                    var node = new NodeConnectionSettings
                    {
                        TlsCertFilePath = Environment.GetEnvironmentVariable("HOME") +
                                          $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/tls.cert".MapAbsolutePath(),
                        MacaroonFilePath = Environment.GetEnvironmentVariable("HOME") +
                                           $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/data/chain/bitcoin/signet/admin.macaroon"
                                               .MapAbsolutePath(),
                        LocalIPPath = (Environment.GetEnvironmentVariable("HOME") +
                                       $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip"
                                           .MapAbsolutePath()),
                        LocalIP = (Environment.GetEnvironmentVariable("HOME") +
                                   $"/plebnet-playground-cluster/volumes/lnd_datadir_{i}/localhostip".MapAbsolutePath())
                            .ReadAllText().Replace("\n", string.Empty),
                        Host = $"playground-lnd-{i}:10009",
                    };
                    data.Add(node);
                }


                File.WriteAllText(saveTo, data.ToJson(), Encoding.UTF8);
            }

            [Test]
            public void ReadJSONConfigFile()
            {
                var saveTo = Environment.GetEnvironmentVariable("HOME") + "/LNDroneController/drone-config.json";
                var text = File.ReadAllText(saveTo, Encoding.UTF8);
                var data = text.FromJson<List<NodeConnectionSettings>>();
                data.PrintDump();
            }
            
            [Test]
            public async Task ListInactiveChannels()
            {
                await NodeConnections.ParallelForEachAsync( async node =>
                {
                    var channels = await node.ListInactiveChannels();
                    if (channels.Count > 0)
                    {
                        $"{node.LocalAlias}:".Print();

                        foreach (var c in channels)
                        {
                            var pubkey = c.RemotePubkey;
                            var alias = string.Empty;
                            try
                            {
                                 alias = await node.GetNodeAliasFromPubKey(pubkey);
                            }
                            catch (Exception e)
                            {
                            }

                           
                            $"\t{alias} - ChanId: {c.ChanId}".Print();
                        }
                        //channels.PrintDump();
                    }
                },1);
            }

            [Test]
            public async Task SharedKeysAreEqual()
            {
                var alice = NodeConnections[0];
                var bob = NodeConnections[1];

                var aliceSharedKey = await alice.DeriveSharedKey(bob.LocalNodePubKey);
                var bobSharedKey = await bob.DeriveSharedKey(alice.LocalNodePubKey);
                Assert.That(aliceSharedKey.SharedKey == bobSharedKey.SharedKey);
                aliceSharedKey.SharedKey.Count().Print();
            }
            
            [Test]
            public async Task SharedKeysAES()
            {
                var alice = NodeConnections[0];
                var bob = NodeConnections[1];

                var aliceSharedKey = await alice.DeriveSharedKey(bob.LocalNodePubKey);
                var bobSharedKey = await bob.DeriveSharedKey(alice.LocalNodePubKey);
                Assert.That(aliceSharedKey.SharedKey == bobSharedKey.SharedKey);
                var message = "Hello World!";
                var (encryptedValue,iv) = EncryptStringToBytes_Aes(message,aliceSharedKey.SharedKey.ToByteArray());
                var dencryptedValue = DecryptStringFromBytes_Aes(encryptedValue,bobSharedKey.SharedKey.ToByteArray(),iv);
               Assert.That(message==dencryptedValue);
            }

            static (byte[] data, byte[] iv) EncryptStringToBytes_Aes(string plainText, byte[] Key)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            // if (IV == null || IV.Length <= 0)
            //     throw new ArgumentNullException("IV");
            byte[] encrypted;
            byte[] IV;
            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                IV =aesAlg.IV ;
                aesAlg.Mode = CipherMode.CBC;
                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return (encrypted, IV);
        }

        static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {

                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            return plaintext;
        }

            [Test]
            public async Task CrossConnectCluster()
            {

                foreach (var baseNode in NodeConnections)
                {
                    
                    foreach (var node in NodeConnections)
                    {
                        var disconnectedChannels = await baseNode.ListInactiveChannels();
                        
                        if (node.LocalNodePubKey != baseNode.LocalNodePubKey  && disconnectedChannels.Any(t=>t.RemotePubkey == node.LocalNodePubKey)) //not self and disconnected
                        {
                            try
                            {
                                //disconnect & reconnect
                                var disconnect = await baseNode.Disconnect(node.LocalNodePubKey);
                            }
                            catch (RpcException e) when (e.StatusCode == StatusCode.Unknown)
                            {
                                e.PrintDump();
                            }

                            try
                            {
                                //disconnect & reconnect
                                var result = await baseNode.Connect(node.ClearnetConnectString, true);
                                result.PrintDump();
                            }
                            catch (RpcException e) when (e.StatusCode == StatusCode.Unknown)
                            {
                                e.PrintDump();
                            }
                        }
                    }
                }
            }
            [Test]
            public async Task GenerateInvoiceAndPayFromOtherServer()
            {
                var invoice = await NodeConnections[0].GenerateInvoice(new Invoice()
                {
                    Value = 10,
                    Memo = "Pay me 10 sats",
                });
                invoice.PrintDump();
                var payInvoice = await NodeConnections[2].PayPaymentRequest(invoice.PaymentRequest,100);
                payInvoice.PrintDump();
                var checkStatus = await NodeConnections[0].CheckInvoiceStatus(new PaymentHash
                {
                    RHash = invoice.RHash
                });
                checkStatus.PrintDump();
                if (checkStatus.State == Invoice.Types.InvoiceState.Settled)
                {
                    Assert.Pass();
                }
                else
                {
                    Assert.Fail();
                }
            }

           
            [Test]
            public async Task CrossPayCluster()
            {
                var nodes = await NodeConnections[0].DescribeGraph();
                //var tasks = new List<Task<Payment>>(); 
                var bag = new ConcurrentBag<Payment>();
                foreach (var baseNode in NodeConnections.GetRandomFromList(5))
                {
                    await NodeConnections.GetRandomFromList(5).ParallelForEachAsync(async node =>
                    {
                        if (node.LocalNodePubKey != baseNode.LocalNodePubKey)
                        {
                            bag.Add(await baseNode.KeysendPayment(node.LocalNodePubKey, 100, timeoutSeconds: 5));
                        }
                    }, maxDegreeOfParallelism: 5);
                }

                var good = bag.Where(x => x.Status == Payment.Types.PaymentStatus.Succeeded).Count();
                var bad = bag.Where(x => x.Status == Payment.Types.PaymentStatus.Failed).Count();
                $"Success: {good}/{bag.Count()} Fail: {bad}  {(1.0 * good / bag.Count * 100)}%".Print();

            }
            [Test]
            public async Task ConnectRandomNodes()
            {
                foreach (var baseNode in NodeConnections)
                {

                    var nodes = await NodeConnections.GetNewRandomNodes(baseNode, 15);

                    Console.WriteLine($"Node: {baseNode.LocalAlias}");

                    foreach (var connectToNode in nodes)
                    {
                        try
                        {
                            Console.WriteLine($"Connecting: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                            var result = await baseNode.Connect(connectToNode.ClearnetConnectString);
                            result.PrintDump();
                        }
                        catch (Grpc.Core.RpcException e)
                        {
                            e.Status.Detail.PrintDump();
                        }
                    }
                }
            }
            [Test]
            //[Ignore("Remove Ignore if you really want to do this")]
            public async Task SpecficNodeOpenChannelsWithRandomNodes()
            {
                List<string> pubkeys = new List<string>();
                NodeConnections.ForEach(x=>pubkeys.Add(x.LocalNodePubKey));

                // 5
                // 6
                // 7
                // 11
                // 13
                // 19
                // 21
                // 22
                // 34
                var baseNode = NodeConnections[22];
                {
                    var graph = (await baseNode.DescribeGraph()).Nodes.Where(x=>!pubkeys.Contains((x.PubKey))).ToList();
                    graph.Count().Print();
                    var nodes = await NodeConnections.GetNewRandomNodes(baseNode,5);

                    Console.WriteLine($"Node: {baseNode.LocalAlias}");

                    foreach (var connectToNode in nodes)
                    {
                        try
                        {
                             
                            Console.WriteLine($"Connecting: {connectToNode.LocalAlias}");
                            var result = await baseNode.Connect( connectToNode.ClearnetConnectString);
                            result.PrintDump(); 
                            try
                            {
                                Console.WriteLine($"Opening: {connectToNode.LocalAlias}  10MSat");
                                var result2 = await baseNode.OpenChannel(connectToNode.LocalNodePubKey, 100000000L, 50000000L);
                                result2.PrintDump();
                            }
                            catch (Grpc.Core.RpcException e)
                            {
                                e.Status.Detail.PrintDump();
                            }
                        }
                        catch (Grpc.Core.RpcException e)
                        {
                            e.Status.Detail.PrintDump();
                        }
                        
                    }
                }
            }
            
            [Test]
    //        [Ignore("Remove Ignore if you really want to do this")]
            public async Task OpenChannelsWithRandomNodes()
            {
                List<string> pubkeys = new List<string>();
                NodeConnections.ForEach(x=>pubkeys.Add(x.LocalNodePubKey));
                foreach (var baseNode in NodeConnections.GetRandomFromList(5))
                {
                  //  var graph = (await baseNode.DescribeGraph()).Nodes.Where(x=>!pubkeys.Contains((x.PubKey))).ToList();
                  //  graph.Count().Print();
                    var nodes = NodeConnections.ToLightningNodes(); //await graph.GetNewRandomNodes(baseNode, 4);

                    Console.WriteLine($"Node: {baseNode.LocalAlias}");

                    foreach (var connectToNode in nodes)
                    {
                        try
                        {
                            if (connectToNode.Addresses.Count < 1)
                            {
                                $"Node has no addresses: {connectToNode.Alias}".Print();
                                continue;
                            }
                            Console.WriteLine($"Connecting: {connectToNode.Alias} : {connectToNode.Addresses.First().Addr}");
                            var result = await baseNode.Connect( connectToNode.PubKey + "@" + connectToNode.Addresses.First().Addr);
                            result.PrintDump(); 
                            try
                            {
                                Console.WriteLine($"Opening: {connectToNode.Alias}  10MSat");
                                var result2 = await baseNode.OpenChannel(connectToNode.PubKey, 10000000L, 5000000L);
                                result2.PrintDump();
                                var result22 = await baseNode.Connect( connectToNode.PubKey + "@" + connectToNode.Addresses.First().Addr);
                            }
                            catch (Grpc.Core.RpcException e)
                            {
                                e.Status.Detail.PrintDump();
                            }
                        }
                        catch (Grpc.Core.RpcException e)
                        {
                            e.Status.Detail.PrintDump();
                        }
                        
                    }
                }
            }
            [Test]
            public async Task ProbePayment()
            {
                var response = await NodeConnections[0].ProbePayment("03ee9d906caa8e8e66fe97d7a76c2bd9806813b0b0f1cee8b9d03904b538f53c4e", 10);
                var result = response.FailureReason.ToString() == "FailureReasonIncorrectPaymentDetails" ? "Online" : response.FailureReason.ToString();
                $"HelloJessica [signet] @ 10sats - {result} - {response.FailureReason}".Print();
            }
            [Test]
            public async Task SendPaymentWithMessage()
            {
                var response = await NodeConnections[0].KeysendPayment("03ee9d906caa8e8e66fe97d7a76c2bd9806813b0b0f1cee8b9d03904b538f53c4e", 10, 10, message: "Hello World!");
                response.PrintDump();
            }
            private static async Task KeySendWithMessage(LNDNodeConnection baseNode, LNDNodeConnection connectToNode)
            {
                try
                {
                    Console.WriteLine($"KeySending: {connectToNode.LocalAlias} : {connectToNode.ClearnetConnectString}");
                    var result = await baseNode.KeysendPayment(connectToNode.LocalNodePubKey, 10, 10, "Hello World!");
                    int i = 0;
                    foreach (var h in result.Htlcs)
                    {
                        i++;
                        $"HTLC Attempt #{i} : Hops {h.Route.Hops.Count} Status:{h.Status}".Print();
                    }
                }
                catch (Grpc.Core.RpcException e)
                {
                    e.Status.Detail.PrintDump();
                    Assert.Fail();
                }
            }
            private static async Task KeySendWithMessage(LNDNodeConnection baseNode, LightningNode connectToNode)
            {
                try
                {
                    Console.WriteLine($"KeySending: {connectToNode.Alias} : {connectToNode.PubKey}");
                    var result = await baseNode.KeysendPayment(connectToNode.PubKey, 10, 10, "Hello World!");
                    int i = 0;
                    foreach (var h in result.Htlcs)
                    {
                        i++;
                        $"HTLC Attempt #{i} : Hops {h.Route.Hops.Count} Status:{h.Status}".Print();
                    }
                }
                catch (Grpc.Core.RpcException e)
                {
                    e.Status.Detail.PrintDump();
                    Assert.Fail();
                }
            }
        }
    }