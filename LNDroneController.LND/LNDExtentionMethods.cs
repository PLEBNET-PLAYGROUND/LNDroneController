using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using LNDroneController.LND;
using Lnrpc;
using ServiceStack;
using ServiceStack.Text;

namespace LNDroneController.Extentions
{

    public static class LNDExtensions
    {
        private static Random r = new Random();

      

        public static async Task<List<LNDNodeConnection>> GetNewRandomNodes(this List<LNDNodeConnection> nodes, LNDNodeConnection baseNode, int count, int maxCycleCount = 1000)
        {
            var response = new List<LNDNodeConnection>();
            var randomMax = nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var existingChannels = await baseNode.GetChannels();
                var found = false;
                var cycleCount = 0;
                while (!found)
                {
                    cycleCount++;
                    var nextRandomNode = nodes[r.Next(randomMax)];
                    //find nodes not in existing list, not self, and not any existing channel
                    if (!response.Contains(nextRandomNode) &&
                        nextRandomNode != baseNode &&
                        !existingChannels.Any(x => x.RemotePubkey == nextRandomNode.LocalNodePubKey))
                    {
                        found = true;
                        response.Add(nextRandomNode);
                    }
                    if (cycleCount >= maxCycleCount)
                        break;
                }
            }
            return response;
        }


        public static async Task<List<LightningNode>> GetNewRandomNodes(this IList<LightningNode> nodes, LNDNodeConnection baseNode, int count, int maxCycleCount = 1000)
        {
            var response = new List<LightningNode>();
            var randomMax = nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var existingChannels = await baseNode.GetChannels();
                var found = false;
                var cycleCount = 0;
                while (!found)
                {
                    cycleCount++;
                    var nextRandomNode = nodes[r.Next(randomMax)];
                    //find nodes not in existing list, not self, and not any existing channel
                    if (!response.Contains(nextRandomNode) &&
                        nextRandomNode.PubKey != baseNode.LocalNodePubKey &&
                        existingChannels.All(x => x.RemotePubkey != nextRandomNode.PubKey))
                    {
                        found = true;
                        response.Add(nextRandomNode);
                    }
                    if (cycleCount >= maxCycleCount)
                        break;
                }
            }
            return response;
        }
        public static LightningNode ToLightningNode(this LNDNodeConnection node)
        {
            return new LightningNode
            {
                Alias = node.LocalAlias,
                PubKey = node.LocalNodePubKey
            };
        }
        public static List<LightningNode> ToLightningNodes(this List<LNDNodeConnection> nodes)
        {
            return nodes.ConvertAll(x => x.ToLightningNode());
        }

        public static (IEnumerable<Channel> localSources, IEnumerable<Channel> remoteTargets) FindRebalanceChannelSet(this List<Channel> channels, long amountToMove)
        {
            var local = channels.FindLocalSources(amountToMove).ToList();
            return (local, channels.FindRemoteTargets(local, amountToMove));
        }
        public static IEnumerable<Channel> FindLocalSources(this IList<Channel> channels, long amountToMove)
        {
            //return channels.Where(x => x.Active && (x.LocalBalance - amountToMove) > LNDChannelLogic.MinLocalBalanceSats && (x.LocalBalance / (double)x.Capacity) >= LNDChannelLogic.MinLocalBalancePercentage);
            return channels.Where(x => x.Active && (x.LocalBalance / (double)x.Capacity) >= LNDChannelLogic.MinLocalBalancePercentage);
        }

        public static IEnumerable<Channel> FindRemoteTargets(this IList<Channel> channels, IList<Channel> excludeChannels, long amountToMove)
        {
           // return channels.Where(x => x.Active && (x.RemoteBalance - amountToMove) > LNDChannelLogic.MinRemoteBalanceSats && (x.RemoteBalance / (double)x.Capacity) >= LNDChannelLogic.MinRemoteBalancePercentage);
            return channels.Where(x => x.Active &&  (x.LocalBalance / (double)x.Capacity) <= LNDChannelLogic.MaxRemoteLocalBalancePercentage && !excludeChannels.Contains(x));
        }

        public static (byte[] data, byte[] iv) EncryptStringToAesBytes(this byte[] ClearData, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (ClearData.Length <= 0)
                throw new ArgumentNullException("ClearData");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            byte[] encrypted;
            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                if (IV != null)
                    IV = aesAlg.IV;
                aesAlg.Mode = CipherMode.CBC;
                // Create an encryptor to perform the stream transform.
                IV = aesAlg.IV;
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(ClearData);
                    }
                    encrypted = msEncrypt.ToArray();
                }
            }

            // Return the encrypted bytes from the memory stream.
            return (encrypted, IV);
        }

        public static byte[] DecryptStringFromBytesAes(this byte[] CipherData, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (CipherData.Length <= 0)
                throw new ArgumentNullException("CipherData");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                aesAlg.Mode = CipherMode.CBC;
                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(CipherData))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        return csDecrypt.ReadFully();
                    }
                }
            }
        }
    }
}

