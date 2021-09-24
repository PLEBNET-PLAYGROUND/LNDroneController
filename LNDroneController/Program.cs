using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ServiceStack;
using ServiceStack.Text;
using LNDroneController.LND;
using System.Threading;
using System.Linq;
using LNDroneController.Types;
namespace LNDroneController
{
    class Program
    {
        private static Random r = new Random();
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Syntax: LNDroneController <full path to drone config file>");
                return;
            }
            var nodeConnections = new List<LNDNodeConnection>();
            var config = File.ReadAllText(args[0],Encoding.UTF8).FromJson<List<NodeConnectionSetings>>();
            foreach(var node in config)
            {
                var nodeConnection = new LNDNodeConnection();
                nodeConnections.Add(nodeConnection);
                if (!node.LocalIPPath.IsNullOrEmpty())
                    node.LocalIP = node.LocalIPPath.ReadAllText();
                nodeConnection.Start(node.TlsCertFilePath, node.MacaroonFilePath, node.Host,node.LocalIP);
            }

            Console.WriteLine("Press ANY key to stop process");
            Console.ReadKey();
        }




    }

}
