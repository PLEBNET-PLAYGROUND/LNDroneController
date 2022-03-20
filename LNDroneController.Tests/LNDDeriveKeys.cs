using LNDroneController.LND;
using NBitcoin;
using NUnit.Framework;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LNDroneController.Tests
{
    public class LNDDeriveKeys
    {
        private LNDNodeConnection Alice;

        [SetUp]
        public void Setup()
        {
            Alice = new LNDNodeConnection(new LNDSettings
            {
                TLSCertBase64 = $"LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0tCk1JSUNLakNDQWRDZ0F3SUJBZ0lRVFNKbXJrWEd2eVptcEVIMG9yc2VNekFLQmdncWhrak9QUVFEQWpBNE1SOHcKSFFZRFZRUUtFeFpzYm1RZ1lYVjBiMmRsYm1WeVlYUmxaQ0JqWlhKME1SVXdFd1lEVlFRREV3dzBaVEZrTkRnMQpaRGd5WkdFd0hoY05Nakl3TXpFM01UUTFPRE0xV2hjTk1qTXdOVEV5TVRRMU9ETTFXakE0TVI4d0hRWURWUVFLCkV4WnNibVFnWVhWMGIyZGxibVZ5WVhSbFpDQmpaWEowTVJVd0V3WURWUVFERXd3MFpURmtORGcxWkRneVpHRXcKV1RBVEJnY3Foa2pPUFFJQkJnZ3Foa2pPUFFNQkJ3TkNBQVNIQ0d5dkJtNktTcnZCRkZjVEJLbEpzaXI0czJEYgpndjFRVi9MM1ZxR2h6c2pySkxJYjY2Q1Eyb2krU2RBWU5FTFJqWUpTODgwZTZ6aGFrODVzbk1oZm80RzdNSUc0Ck1BNEdBMVVkRHdFQi93UUVBd0lDcERBVEJnTlZIU1VFRERBS0JnZ3JCZ0VGQlFjREFUQVBCZ05WSFJNQkFmOEUKQlRBREFRSC9NQjBHQTFVZERnUVdCQlQxTDJDYW5iYVhjR0FtdmV1MU5HRG83STR6dlRCaEJnTlZIUkVFV2pCWQpnZ3cwWlRGa05EZzFaRGd5WkdHQ0NXeHZZMkZzYUc5emRJSUVkVzVwZUlJS2RXNXBlSEJoWTJ0bGRJSUhZblZtClkyOXVib2NFZndBQUFZY1FBQUFBQUFBQUFBQUFBQUFBQUFBQUFZY0VyQkVBQW9jRUNoVUVaakFLQmdncWhrak8KUFFRREFnTklBREJGQWlCenNsYUtaamxFK1UvWnNvc2o1WHRJRk9GK0dvWlRSY3RhTGZ0b3dTb09wUUloQU5UZQpGUDVRbW9qcENRcEVLdWRjcXBLUkFCZlhIdHNKRFhBd3N1KzY3dTNUCi0tLS0tRU5EIENFUlRJRklDQVRFLS0tLS0K",
                MacaroonBase64 = $"AgEDbG5kAvgBAwoQz8x2/BG8y02tK1Iuz+tX9BIBMBoWCgdhZGRyZXNzEgRyZWFkEgV3cml0ZRoTCgRpbmZvEgRyZWFkEgV3cml0ZRoXCghpbnZvaWNlcxIEcmVhZBIFd3JpdGUaIQoIbWFjYXJvb24SCGdlbmVyYXRlEgRyZWFkEgV3cml0ZRoWCgdtZXNzYWdlEgRyZWFkEgV3cml0ZRoXCghvZmZjaGFpbhIEcmVhZBIFd3JpdGUaFgoHb25jaGFpbhIEcmVhZBIFd3JpdGUaFAoFcGVlcnMSBHJlYWQSBXdyaXRlGhgKBnNpZ25lchIIZ2VuZXJhdGUSBHJlYWQAAAYgpsoEidbnsavbWJ2Ew7YSP2aVE8+aUcM3UewejfM3cYs=",
                GrpcEndpoint = $"https://10.21.4.102:10009",
            });
        }
        [Test]
        public async Task LNDKeysTest()
        {
            var aezeed = "about trigger sport only answer icon panda canal wise age buffalo destroy mule alter team weasel ice one pear heavy census pony fiscal acquire"; //no logic for this just reference

            var xprv = "xprv9s21ZrQH143K3EH5SqkxGeb1prC9TsuVnu5GcNva3Bgvp7rRMPUUDeYBrQuxZorFXE3L9XZtqm95MPLpuSkp8q2WtQHap9NDcHDsNXu2Pep";
            var lndPubKey = "0257780624efb6fd6f49fe06ab38857b5afea7c2e75923a1da6808f01fd217f51b";
            var (lndPrivate, lndPub) = LNDTools.DeriveLNDNodeKeys(xprv, false);
            Assert.That(lndPub.ToHex() == lndPubKey);
        }

        [Test]
        public async Task DeriveKeys()
        {
            var blob = File.ReadAllText("OnionBlob.json").FromJson<byte[]>();
            var decoder = new OnionBlobDecoder(Alice, blob);
            var sessionKey = decoder.SessionKey;

            var xprv = "xprv9s21ZrQH143K3EH5SqkxGeb1prC9TsuVnu5GcNva3Bgvp7rRMPUUDeYBrQuxZorFXE3L9XZtqm95MPLpuSkp8q2WtQHap9NDcHDsNXu2Pep";
            var (lndPrivate, lndPub) = LNDTools.DeriveLNDNodeKeys(xprv, false);
            var shared = LNDTools.DeriveSharedSecret(sessionKey, lndPrivate);
            var lndSharedSecret = (await Alice.DeriveSharedKey(sessionKey.ToHex())).SharedKey.ToByteArray();

            Assert.That(shared.ToHex().Equals(lndSharedSecret.ToHex()));
        }
    }
}
