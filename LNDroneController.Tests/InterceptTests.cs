using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using LNDroneController.LND;
using ServiceStack;
using ServiceStack.Text;
using Routerrpc;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using System.Linq;
using LNBolt;

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
            var interceptor = new LNDSimpleHtlcInterceptorHandler(Carol, SettleBeforeDestinationIfKeysend);
            await Task.Delay(1000 * 10000);
        }

        private async Task<ForwardHtlcInterceptResponse> SettleBeforeDestinationIfKeysend(ForwardHtlcInterceptRequest data)
        {
            Debug.Print(data.Dump());
            var onionBlob = data.OnionBlob.ToByteArray();
            var decoder = new OnionBlob(onionBlob);
            var sharedSecret = (await Alice.DeriveSharedKey(decoder.EphemeralPublicKey.ToHex())).SharedKey.ToByteArray();
            var x = decoder.Peel(sharedSecret, null, data.PaymentHash.ToByteArray());

            Debug.Print(x.hopPayload.Dump());
            x.PrintDump();
            //await decoder.Decode();
            //decoder.PrintDump();
            if (x.hopPayload.PaymentData != null || !x.hopPayload.OtherTLVs.Any(x=>x.Type == 5482373484))
            {
                return new ForwardHtlcInterceptResponse
                {
                    Action = ResolveHoldForwardAction.Resume,
                    IncomingCircuitKey = data.IncomingCircuitKey,
                };
            }
            else
            {
                var keySendPreimage = x.hopPayload.OtherTLVs.First(x => x.Type == 5482373484).Value;
                return new ForwardHtlcInterceptResponse
                {
                    Action = ResolveHoldForwardAction.Settle,
                    IncomingCircuitKey = data.IncomingCircuitKey,
                    Preimage = Google.Protobuf.ByteString.CopyFrom(keySendPreimage)
                };
            }
            

           
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