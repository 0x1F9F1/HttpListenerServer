using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

namespace HttpListenerServer
{
    public class HttpServer : IDisposable
    {
        private readonly HttpListener _httpListener;
        private Thread _listenerThread;
        private readonly Handler _requestHandler;

        public HttpServer(string rootFolder = @"Files\", bool relative = true)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;

            if (!rootFolder.EndsWith(@"\")) { rootFolder += @"\";}
            _listenerThread = new Thread(ListenerThread);
            _requestHandler = new Handler(relative ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rootFolder) : rootFolder);

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
            Console.WriteLine($"{DateTime.Now:R} | {data}");
        }


        public void Start()
        {
            try
            {
                Log("Starting Server");
                if (!_httpListener.IsListening)
                    _httpListener.Start();
                if (!_listenerThread.IsAlive)
                    _listenerThread = new Thread(ListenerThread);
                    _listenerThread.Start();

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
                Log("Stopping Server");
                if (_httpListener.IsListening)
                    _httpListener.Stop();
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