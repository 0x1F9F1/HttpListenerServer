using System;
using HttpListenerServer;

namespace ConsoleServerTest
{
    internal static class Program
    {
        private static void Main()
        {
            var httpServer = new HttpServer(@"D:\ServerFiles\", false);
            httpServer.Start();
            Console.ReadKey();
            httpServer.Stop();
        }
    }
}