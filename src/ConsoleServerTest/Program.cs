using System;
using HttpListenerServer;

namespace ConsoleServerTest
{
    internal static class Program
    {
        private static void Main()
        {
            Console.Title = "Brick's C# Server";
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Clear();

            var httpServer = new HttpServer(@"D:\ServerFiles\", false, false);
            httpServer.Start();
            Console.ReadKey();
            httpServer.Stop();
        }
    }
}