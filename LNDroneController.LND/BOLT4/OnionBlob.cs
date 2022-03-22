using Org.BouncyCastle.Math;
using ServiceStack;
using System;
using System.Diagnostics;
using System.Linq;
using Kermalis.EndianBinaryIO;
using System.Collections.Generic;
using ServiceStack.Text;
using System.Security.Cryptography;
namespace LNDroneController.LND
{
    public class OnionBlob
    {
        public byte Version { get; internal set; }
        public byte[] EphemeralPublicKey { get; internal set; }
        public byte[] HopPayloads { get; internal set; }
        public byte[] NextHmac { get; internal set; }
        public byte[] RawOnion
        {
            get
            {
                return (new byte[] { Version }).Concat(EphemeralPublicKey)
                      .Concat(HopPayloads)
                      .Concat(NextHmac).ToArray();
            }
        }
        private static readonly int ONION_PACKET_LENGTH = 1300;
        private static readonly int HMAC_LENGTH = 32;

        public OnionBlob() { }
        public OnionBlob(byte[] rawOnionBlob)
        {
            Version = rawOnionBlob[0];
            EphemeralPublicKey = rawOnionBlob[1..34];
            HopPayloads = rawOnionBlob[34..1334];
            NextHmac = rawOnionBlob[1334..1366];
        }

       
        public static OnionBlob ConstructOnion(List<byte[]> sharedSecrets, List<HopPayload> payloads, byte[] firstHopPublicKey, byte[] associatedData = null)
        {
            var nextHmac = new byte[HMAC_LENGTH];
            var filler = GenerateFiller(sharedSecrets, payloads);
            Debug.Print($"Filler: {filler.ToHex()}");
            var hopPayloads = new byte[ONION_PACKET_LENGTH];

            for (int i = sharedSecrets.Count - 1; i >= 0; i--)
            {
                Debug.Print("Onion round {i}");
                var currentSharedSecret = sharedSecrets[i];
                var currentPayload = payloads[i];
                var rhoKey = LNDTools.GenerateRhoKey(currentSharedSecret);
                var muKey = LNDTools.GenerateMuKey(currentSharedSecret);

                var shiftSize = currentPayload.SphinxSize + HMAC_LENGTH;
                // right-shift onion packet bytes: JS: const filler = Buffer.alloc(fillerSize, 0);
                //hopPayloads.copyWithin(shiftSize, 0);
                hopPayloads.CopyWithin(shiftSize, 0);
                var currentHopData = currentPayload.ToDataBuffer().Concat(nextHmac).ToArray();

                //     hopPayloads.CopyTo(currentHopData,0); //TODO: not sure if this is equiv JS: 	currentHopData.copy(hopPayloads);
                currentHopData.CopyTo(hopPayloads, 0);
                var streamBytes = LNDTools.GenerateCipherStream(new byte[ONION_PACKET_LENGTH], rhoKey, new byte[12]);

                Debug.Print($"Stream Bytes: {streamBytes.ToHex()}");
                Debug.Print($"Hop Data: {currentHopData.ToHex()}");

                //XOR the onion packet with stream
                for (var j = 0; j < 1300; j++)
                {
                    // let's not XOR anything for now
                    hopPayloads[j] = (byte)(hopPayloads[j] ^ streamBytes[j]);
                }
                if (i == sharedSecrets.Count - 1)
                {
                    filler.CopyTo(hopPayloads, hopPayloads.Length - filler.Length);
                }

                Debug.Print($"Raw Onion: {hopPayloads.ToHex()}");
                var hmacData = hopPayloads;
                if (associatedData != null && associatedData.Length > 0)
                {
                    hmacData = hmacData.Concat(associatedData).ToArray();
                }
                nextHmac = LNDTools.CalculateHMAC(muKey, hmacData);
            }
            return new OnionBlob
            {
                EphemeralPublicKey = firstHopPublicKey,
                HopPayloads = hopPayloads,
                NextHmac = nextHmac,
                Version = 0,
            };
        }

        public static byte[] GenerateFiller(List<byte[]> sharedSecrets, List<HopPayload> payloads)
        {
            var payloadSizes = payloads.Select(x => x.SphinxSize + HMAC_LENGTH).ToArray();
            var totalPayloadSize = payloadSizes.Sum(x => x);
            var lastPayloadSize = payloadSizes[payloadSizes.Length-1];

            var fillerSize = totalPayloadSize - lastPayloadSize;
            var filler = new byte[fillerSize];
            var trailingPayloadSize = 0;
            for (int i = 0; i < sharedSecrets.Count()-1; i++)
            {
                Debug.Print($"Filler round {i}");
                var currentSharedSecret = sharedSecrets[i];
                var currentPayloadSize = payloadSizes[i];

                Debug.Print($"Shared secret {currentSharedSecret.ToHex()}");
                var rhoKey = LNDTools.GenerateRhoKey(currentSharedSecret);
                Debug.Print($"Shared key {rhoKey.ToHex()}");

                var fillerSourceStart = ONION_PACKET_LENGTH - trailingPayloadSize;
                var fillerSourceEnd = ONION_PACKET_LENGTH + currentPayloadSize;

                var streamLength = ONION_PACKET_LENGTH * 2;            

                var streamBytes = LNDTools.GenerateCipherStream(new byte[streamLength], rhoKey, new byte[12]);                
                for (var j = fillerSourceStart; j < fillerSourceEnd; j++)
                {
                    var fillerIndex = j - fillerSourceStart;
                    var fillerValue = filler[fillerIndex];
                    var streamValue = streamBytes[j];
                    filler[fillerIndex] = (byte)(fillerValue ^ streamValue);
                }
                trailingPayloadSize += currentPayloadSize;
            }

            return filler;
        }

        public (HopPayload hopPayload, OnionBlob nextSphinx) Peel(byte[] sharedSecret = null, byte[] hopPrivateKey = null, byte[] associatedData = null)
        {
            if (sharedSecret != null && hopPrivateKey != null)
            {
                throw new Exception("sharedSecret XOR hopPrivateKey must be provided");
            }
            if (hopPrivateKey != null)
            {
                sharedSecret = LNDTools.DeriveSharedSecret(EphemeralPublicKey, hopPrivateKey);
            }

            var rhoKey = LNDTools.GenerateRhoKey(sharedSecret);
            var muKey = LNDTools.GenerateMuKey(sharedSecret);

            var data = HopPayloads;
            if (associatedData != null)
            {
                data = data.Concat(associatedData).ToArray();
            }

            var currentHmac = LNDTools.CalculateHMAC(muKey, data);
            Debug.Print($"Excepted HMAC: {NextHmac.ToHex()}");
            Debug.Print($"Actual HMAC: {currentHmac.ToHex()}");
            if (currentHmac.ToHex() != NextHmac.ToHex())
            {
                throw new Exception("HMAC mismatch on peel");
            }

            var extendedPayload = HopPayloads.Concat(new byte[ONION_PACKET_LENGTH]).ToArray();
            var streamLength = ONION_PACKET_LENGTH * 2;
            var streamBytes = LNDTools.GenerateCipherStream(new byte[streamLength], rhoKey, new byte[12]);

            extendedPayload = LNDTools.Xor(extendedPayload, streamBytes);

            var hopPayload = HopPayload.ParseSphinx(extendedPayload);

            var hmacIndex = hopPayload.SphinxSize;
            var nextPayloadIndex = hmacIndex + HMAC_LENGTH;

            var nextHmac = extendedPayload[hmacIndex..nextPayloadIndex];
            OnionBlob nextSphinx = null;
            if (nextHmac.ToHex() != (new byte[HMAC_LENGTH]).ToHex())
            {
                var nextPayload = extendedPayload[nextPayloadIndex..(nextPayloadIndex + ONION_PACKET_LENGTH)];
                var nextEphemeralPublicKey = CalculateNextEphemeralPublicKey(sharedSecret,EphemeralPublicKey);
                nextSphinx = new OnionBlob
                {
                    Version = this.Version,
                    EphemeralPublicKey = nextEphemeralPublicKey,
                    HopPayloads = nextPayload,
                    NextHmac = nextHmac,
                };
            }


            return (hopPayload, nextSphinx);

        }

        private byte[] CalculateNextEphemeralPublicKey(byte[] sharedSecret, byte[] ephemeralPublicKey)
        {
            var blindingFactor = LNDTools.GenerateBlindingFactor(ephemeralPublicKey,sharedSecret);
            return LNDTools.GenerateBlindedSessionKey(sharedSecret, blindingFactor);
        }
    }
}
