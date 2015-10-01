using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Threading;

namespace HttpServer
{
    public class HttpServer : IDisposable
    {
        public readonly string RootFolder;
        private readonly HttpListener _httpListener;
        private Thread _listenerThread;
        private Handler _requestHandler;

        public HttpServer(string rootFolder = @"Files\", bool relative = true)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;

            if (!rootFolder.EndsWith(@"\")) { rootFolder += @"\";}
            RootFolder = relative ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rootFolder) : rootFolder;
            _listenerThread = new Thread(ListenerThread);
            _requestHandler = new Handler(RootFolder);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(@"http://*:80/");
        }

        private void ListenerThread()
        {
            while (_httpListener.IsListening)
            {
                try
                {
                    while (_httpListener.IsListening)
                    {
                        var context = _httpListener.GetContext();
                        ThreadPool.QueueUserWorkItem(HandleRequest, context);
                    }
                }
                catch (Exception e)
                {
                    Log(e.Message);
                }   
            }
        }

        private void HandleRequest(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;
            if (_requestHandler.HandleIcon(context)) { }
            else if (_requestHandler.HandleFile(context)) { }
            else if(_requestHandler.HandleDirectory(context)) { }
            else if (_requestHandler.HandleOther(context)) { }
            else { context.Response.Abort(); }
        }

        private static void Log(object data)
        {
            Debug.WriteLine($"{DateTime.Now:R} | {data}");
        }


        public void Start()
        {
            try
            {

            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        public void Stop()
        {
            try
            {

            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        public void Abort()
        {
            try
            {

            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        public void Dispose()
        {
            _httpListener.Close();
        }

        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture) ? assemblyName.Name + ".dll" : $@"{assemblyName.CultureInfo}\{assemblyName.Name}.dll"))
            {
                if (stream == null)
                    return null;

                var assemblyRawBytes = new byte[stream.Length];
                stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
                return Assembly.Load(assemblyRawBytes);
            }
        }
    }
}