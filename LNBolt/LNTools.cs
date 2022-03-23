using NBitcoin;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.IO;

namespace LNBolt
{
    public static class LNTools
    {
        public static readonly byte[] Rho = { 0x72, 0x68, 0x6F };
        public static readonly byte[] Mu = { 0x6d, 0x75 };
        public static readonly byte[] Um = { 0x75, 0x6D };
        public static readonly byte[] Nonce = new byte[12];

        private static System.Security.Cryptography.SHA256 SHA256Hash = System.Security.Cryptography.SHA256.Create();

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

        public static byte[] DeriveSharedSecret(byte[] publicKey, byte[] privateKey)
        {
            ECKeyPair publicKeyEC = new ECKeyPair(publicKey, false);
            ECKeyPair privateKeyEC = new ECKeyPair(privateKey, true);
            var ecdhResult = publicKeyEC.PublicKeyParameters.Q.Multiply(privateKeyEC.PrivateKey.D);
            var computedShared = SHA256Hash.ComputeHash(ecdhResult.GetEncoded());
            return computedShared;
        }
        public static List<byte[]> CalculatedSharedSecrets(byte[] sessionKey, List<byte[]> hopPubKeys)
        {
            var hopSecrets = new List<byte[]>();
            var ephemeralPrivateKey = sessionKey;
            for (int i = 0; i < hopPubKeys.Count; i++)
            {
                Debug.Print($"SS Round: {i}");
                var sharedSecretKey = DeriveSharedSecret(hopPubKeys[i], ephemeralPrivateKey);
                hopSecrets.Add(sharedSecretKey);

                if (i >= hopPubKeys.Count)
                    break;
                ECKeyPair privateKeyEC = new ECKeyPair(ephemeralPrivateKey, true);
                var ephemeralPublicKey = privateKeyEC.PublicKeyCompressed;
                Debug.Print($"ephemeralPrivateKey Private: {ephemeralPrivateKey.ToHex()}");
                Debug.Print($"ephemeralPrivateKey Public: {ephemeralPublicKey.ToHex()}");

                var blindingFactor = GenerateBlindingFactor(ephemeralPublicKey, sharedSecretKey);
                ephemeralPrivateKey = GenerateBlindedSessionKey(ephemeralPrivateKey, blindingFactor);
            }
            return hopSecrets;
        }

        public static byte[] GenerateBlindingFactor(byte[] pubkey, byte[] sharedSecret)
        {
            var blindingFactorPreimage = pubkey.Concat(sharedSecret).ToArray();
            return SHA256Hash.ComputeHash(blindingFactorPreimage);
        }

        public static byte[] GenerateBlindedSessionKey(byte[] sessionKey, byte[] blindingFactor)
        {
            ECKeyPair ecSessionKey = new ECKeyPair(sessionKey, true);
            BigInteger n = new BigInteger("115792089237316195423570985008687907852837564279074904382605163141518161494337");
            return ecSessionKey.PrivateKey.D.Multiply(new BigInteger(1, blindingFactor)).Mod(n).ToByteArray();
        }


        public static byte[] GenerateCipherStream(byte[] data, byte[] key, byte[] nonce)
        {
            //using (var chacha20 = new ChaCha20(key, nonce, 0))
            //{
            //    return chacha20.EncryptBytes(data);
            //}

            var engine = new ChaCha7539Engine();

            engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));
            var cipherText = new byte[data.Length];

            engine.ProcessBytes(data, 0, data.Length, cipherText, 0);
            return cipherText;
        }


        public static byte[] Xor(byte[] target, byte[] xorData)
        {
            var result = target.ToArray();
            if (xorData.Length > target.Length)
            {
                throw new ArgumentException($"{nameof(target)}.Length needs to be >= {nameof(xorData)}.Length");
            }

            for (int i = 0; i < xorData.Length; i++)
            {
                result[i] = (byte)(target[i] ^ xorData[i]);
            }

            return result;
        }

        public static byte[] CalculateHMAC(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        public static byte[] GenerateRhoKey(byte[] sharedSecret)
        {
            return CalculateHMAC(Rho, sharedSecret);
        }

        public static byte[] GenerateMuKey(byte[] sharedSecret)
        {
            return CalculateHMAC(Mu, sharedSecret);
        }

        public static byte[] GenerateUmKey(byte[] sharedSecret)
        {
            return CalculateHMAC(Um, sharedSecret);
        }
    }
}