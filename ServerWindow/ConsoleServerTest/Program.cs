using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HttpListenerServer;

namespace ConsoleServerTest
{
    static class Program
    {
        static void Main(string[] args)
        {
            HttpServer httpServer = new HttpServer(@"D:\ServerFiles\", false);
            httpServer.Start();
            Console.ReadKey();
            httpServer.Stop();
        }
    }
}
