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
using Routerrpc;
//using System.Security.Cryptography;
using Waher.Security.ChaChaPoly;
using System.Buffers.Binary;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Kermalis.EndianBinaryIO;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace LNDroneController.Tests
{
    public class InterceptTests
    {
        private LNDNodeConnection Carol;
        private LNDNodeConnection Alice;

        [SetUp]
        public void Setup()
        {
            Carol = new LNDNodeConnection(new LNDSettings
            {

                TLSCertPath = @"C:\Users\rjs\.polar\networks\1\volumes\lnd\carol\tls.cert",
                MacaroonPath = @"C:\Users\rjs\.polar\networks\1\volumes\lnd\carol\data\chain\bitcoin\regtest\admin.macaroon",
                GrpcEndpoint = $"https://127.0.0.1:10008",
            });
            Alice = new LNDNodeConnection(new LNDSettings
            {

                TLSCertPath = @"C:\Users\rjs\.polar\networks\1\volumes\lnd\alice\tls.cert",
                MacaroonPath = @"C:\Users\rjs\.polar\networks\1\volumes\lnd\alice\data\chain\bitcoin\regtest\admin.macaroon",
                GrpcEndpoint = $"https://127.0.0.1:10004",
            });
        }

        [Test]
        public async Task TestInterception()
        {
            var interceptor = new LNDSimpleHtlcInterceptorHandler(Carol, DoSomething);
            await Task.Delay(1000 * 10000);
        }
 
        [Test]
        public async Task DecodeOnionBlob()
        {
            var blob = File.ReadAllText("OnionBlob.json").FromJson<byte[]>();
            var decoder = new OnionBlobDecoder(Alice, blob);
            await decoder.Decode();
        }


        private async Task<ForwardHtlcInterceptResponse> DoSomething(ForwardHtlcInterceptRequest data)
        {
            Debug.Print(data.Dump());
            var onionBlob = data.OnionBlob.ToByteArray();
            var decoder = new OnionBlobDecoder(Alice, onionBlob);
            await decoder.Decode();
            decoder.PrintDump();

            return new ForwardHtlcInterceptResponse
            {
                Action = ResolveHoldForwardAction.Resume,
                IncomingCircuitKey = data.IncomingCircuitKey,
            };
        }
    }

    public class OnionBlobDecoder
    {
        public LNDNodeConnection Node { get; private set; }
        public byte Version { get; private set; }
        public byte[] SessionKey { get; private set; }
        public byte[] Payload { get; private set; }
        public byte[] HMAC { get; private set; }
        public byte[] ComputedHMAC { get; private set; }

        //Derived stuff
        public byte[] SharedKey { get; private set; }
        public byte[] DecodedPayload { get; private set; }

        public OnionBlobDecoder(LNDNodeConnection decoderNode, byte[] onionBlob)
        {
            Node = decoderNode;
            Version = onionBlob[0];
            SessionKey = onionBlob[1..34];
            Payload = onionBlob[33..1333];
            HMAC = onionBlob[1333..1365];
        }

        private static readonly byte[] Rho = { 0x72, 0x68, 0x6F };
        private static readonly byte[] Mu = { 0x6d, 0x75 };
        private static readonly byte[] Nonce = new byte[12];

        public async Task Decode()
        {

            var stream = new MemoryStream();
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.Write(SessionKey);
            var swappedSessionKey = stream.ToArray();
            var sharedKeyResponseSwap = await Node.DeriveSharedKey(swappedSessionKey.ToHex());
            var SharedKeySwap = sharedKeyResponseSwap.SharedKey.ToByteArray();
            var sharedKeyResponse = await Node.DeriveSharedKey(SessionKey.ToHex());
            SharedKey = sharedKeyResponse.SharedKey.ToByteArray();
            var muKey = GenerateKey(Mu, SharedKey);
            var hmac = HMAC;
            var computedHMAC = CalculateMac(muKey, Payload);


            var rhoKey = GenerateKey(Rho, SharedKey);
            var chaChaDecoder = new ChaCha20(SharedKey, 0, Nonce);
            var xordata = chaChaDecoder.EncryptOrDecrypt(rhoKey);
            var deobsucated = Xor(Payload, rhoKey);


            (ComputedHMAC).PrintDump();
        }

        private byte[] GenerateKey(byte[] key, byte[] sharedSecret)
        {
            Hmac.Key = key;
            return Hmac.ComputeHash(sharedSecret);
        }


        private static readonly HMACSHA256 Hmac = new HMACSHA256();

        private byte[] CalculateMac(byte[] key, byte[] data)
        {
            Hmac.Key = key;
            return Hmac.ComputeHash(data);
        }

        private byte[] Xor(byte[] target, byte[] xorData)
        {
            if (xorData.Length > target.Length)
            {
                throw new ArgumentException($"{nameof(target)}.Length needs to be >= {nameof(xorData)}.Length");
            }

            for (int i = 0; i < xorData.Length; i++)
            {
                target[i] = (byte)(target[i] ^ xorData[i]);
            }

            return target;
        }
    }


    public class ECKeyPair : IComparable<ECKeyPair>
    {
        public static readonly X9ECParameters Secp256K1 = ECNamedCurveTable.GetByName("secp256k1");
        public static readonly ECDomainParameters DomainParameter = new ECDomainParameters(Secp256K1.Curve, Secp256K1.G, Secp256K1.N, Secp256K1.H);

        private readonly ECKeyParameters _key;
        public ECPrivateKeyParameters PrivateKey => _key as ECPrivateKeyParameters;
        public byte[] PrivateKeyData => PrivateKey.D.ToByteArrayUnsigned();
        public bool HasPrivateKey => _key is ECPrivateKeyParameters;

        public byte[] PublicKeyCompressed
        {
            get
            {
                Org.BouncyCastle.Math.EC.ECPoint ecPoint = PublicKeyParameters.Q.Normalize();
                return Secp256K1.Curve.CreatePoint(ecPoint.XCoord.ToBigInteger(), ecPoint.YCoord.ToBigInteger())
                    .GetEncoded(true);
            }
        }

        public ECKeyPair(String key) : this(key, false)
        {
        }

        public ECKeyPair(String key, bool isPrivate) : this(Convert.FromHexString(key), isPrivate)
        {
        }

        public ECKeyPair(Span<byte> key, bool isPrivate) : this(key.ToArray(), isPrivate)
        {
        }

        public ECKeyPair(byte[] key, bool isPrivate)
        {
            if (isPrivate)
            {
                _key = new ECPrivateKeyParameters(new BigInteger(1, key), DomainParameter);
            }
            else
            {
                _key = new ECPublicKeyParameters("EC", Secp256K1.Curve.DecodePoint(key), DomainParameter);
            }
        }

        public ECPublicKeyParameters PublicKeyParameters
        {
            get
            {
                if (_key is ECPublicKeyParameters)
                {
                    return (ECPublicKeyParameters)_key;
                }

                return new ECPublicKeyParameters("EC", Secp256K1.G.Multiply(PrivateKey.D), DomainParameter);
            }
        }
        public int CompareTo(ECKeyPair other)
        {
            if (other == null)
                return 1;

            bool publicKeyCompare = !other.HasPrivateKey || !HasPrivateKey;

            var thisHex = publicKeyCompare ? PublicKeyCompressed.ToHex() : PrivateKeyData.ToHex();
            var otherHex = publicKeyCompare ? other.PublicKeyCompressed.ToHex() : other.PrivateKeyData.ToHex();
            return String.Compare(thisHex, otherHex, StringComparison.Ordinal);
        }

        public static bool operator <(ECKeyPair e1, ECKeyPair e2)
        {
            return e1.CompareTo(e2) < 0;
        }

        public static bool operator >(ECKeyPair e1, ECKeyPair e2)
        {
            return e1.CompareTo(e2) > 0;
        }
    }
}