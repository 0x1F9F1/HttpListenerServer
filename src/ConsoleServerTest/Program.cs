using System;
using HttpListenerServer;

namespace ConsoleServerTest
{
    static class Program
    {
        static void Main()
        {
            var httpServer = new HttpServer(@"D:\ServerFiles\", false);
            httpServer.Start();
            Console.ReadKey();
            httpServer.Stop();
        }
    }
}
