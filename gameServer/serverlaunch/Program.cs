using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Server;
using System.IO;

namespace TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            int maximumClient = 1000;
            String ipAddress = "192.168.1.53";
            int portnum = 2018;

            if (args.Length>0 && args[0] != null)
            {
                ipAddress = args[0];
            }

            Console.WriteLine("Maximum client available");
            maximumClient = Int32.Parse(Console.ReadLine());
            Network network = new Network(maximumClient);
            Console.WriteLine(Directory.GetCurrentDirectory());
            network.CreateDirectory(@"/home/jwkim/gameServer/TestDB");

            Console.WriteLine("Starting Server at IpAddress:"+ipAddress+  "with Maximum clients:"+maximumClient);
            
            network.Start_Listen(ipAddress, portnum);
        }
    }
}
