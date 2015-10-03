using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HttpListenerServer
{
    public sealed class HttpServer : IDisposable
    {
        private readonly SafeHandle _handle = new SafeFileHandle(IntPtr.Zero, true);
        private readonly HttpListener _httpListener;
        private readonly Handler _requestHandler;
        private bool _disposed;
        private Thread _listenerThread;

        public HttpServer(string rootFolder = @"Files\", bool relative = true)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;

            if (!rootFolder.EndsWith(@"\"))
            {
                rootFolder += @"\";
            }
            _listenerThread = new Thread(ListenerThread);
            _requestHandler =
                new Handler(relative ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rootFolder) : rootFolder);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(@"http://*:80/");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~HttpServer()
        {
            Dispose(false);
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
                    Log($"[Error] {e.Message}");
                }
            }
        }

        private void HandleRequest(object state)
        {
            var context = (HttpListenerContext) state;
            var localPath = context.Request.Url.LocalPath;
            Log($"[Request] {context.Request.Url.LocalPath}");
            var requestType = _requestHandler.GetRequestType(localPath);
            switch (requestType)
            {
                case Handler.RequestType.Icon:
                    _requestHandler.HandleIcon(context);
                    break;
                case Handler.RequestType.File:
                    _requestHandler.HandleFile(context);
                    break;
                case Handler.RequestType.Folder:
                    _requestHandler.HandleDirectory(context);
                    break;
                case Handler.RequestType.Other:
                    _requestHandler.HandleOther(context);
                    break;
                default:
                    throw new Exception("Invalid request type.");
            }
        }

        private static void Log(object data)
        {
            Debug.WriteLine($"{DateTime.Now:R} | [Main] {data}");
            Console.WriteLine($"{DateTime.Now:R} | [Main] {data}");
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
                Log($"[Error] {e.Message}");
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
                Log($"[Error] {e.Message}");
            }
        }

        public void Abort()
        {
            try
            {
                _httpListener.Abort();
                _listenerThread.Abort();
            }
            catch (Exception e)
            {
                Log($"[Error] {e.Message}");
            }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _httpListener.Close();
                _handle.Dispose();
            }

            _disposed = true;
        }

        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);

            using (
                var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture)
                            ? assemblyName.Name + ".dll"
                            : $@"{assemblyName.CultureInfo}\{assemblyName.Name}.dll"))
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