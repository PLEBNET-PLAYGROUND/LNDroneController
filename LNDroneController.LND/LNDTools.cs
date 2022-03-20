using NBitcoin;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LNDroneController.LND
{
    public static class LNDTools
    {
        public static (byte[] privateKey, byte[] publicKey) DeriveLNDNodeKeys(string xprv, bool isMainnet = true)
        {
            var extKey = ExtKey.Parse(xprv, Network.Main); //it is signet 
            var index = isMainnet ? 0 : 1; //1 for testnet/signet
            var lndPath = $"m/1017'/{index}'/6'/0/0"; 
            var lndKey = extKey.Derive(KeyPath.Parse(lndPath));
            var lndPub = lndKey.GetPublicKey();
            var lndPrivate = lndKey.PrivateKey.ToBytes();
            return (lndPrivate, lndPub.ToBytes());
        }

        public static byte[] DeriveSharedSecret(byte[] sessionPubKey, byte[] lndPrivateKey)
        {
            ECKeyPair ecSessionKey = new ECKeyPair(sessionPubKey, false);
            ECKeyPair ecLNDKey = new ECKeyPair(lndPrivateKey, true);
            var ecdhResult = ecSessionKey.PublicKeyParameters.Q.Multiply(ecLNDKey.PrivateKey.D);
            var sha256Hash = System.Security.Cryptography.SHA256.Create();
            var computedShared = sha256Hash.ComputeHash(ecdhResult.GetEncoded());
            return computedShared;
        }

    }

    internal class ECKeyPair : IComparable<ECKeyPair>
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
