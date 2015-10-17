using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using MimeTypes;

namespace HttpListenerServer
{
    public class Handler
    {
        #region RequestType enum

        public enum RequestType
        {
            Icon,
            File,
            Directory,
            Other
        }

        #endregion

        //public enum FileSize
        //{
        //    Byte = 1024^0,
        //    Kilobyte = 1024^1,
        //    Megabyte = 1024^2,
        //    Gigabyte = 1024^3,
        //    Terabyte = 1024^4,
        //}

        private const int BufferSize = 4096;
        private readonly byte[] _compressedIconBytes;
        private readonly string _directoryTemplate;
        private readonly string _errorTemplate;
        private readonly string _folderRoot;
        private readonly byte[] _iconBytes;
        private readonly bool _showFolderSize;

        public Handler(string root, bool showFolderSize)
        {
            _folderRoot = root;
            _showFolderSize = showFolderSize;

            //Try and copy favicon.ico, Directory.html and Error.html from embedded resources to their respective files, if they dont allready exist.
            CreateFileFromResource("favicon.ico");
            CreateFileFromResource("Directory.html");
            CreateFileFromResource("Error.html");

            _iconBytes = File.ReadAllBytes("favicon.ico"); //Load favicon.ico into memory.
            _compressedIconBytes = Compress(_iconBytes); //Load a compressed version of favicon.ico into memory, to save bandwitdth/cpu processing when possible.
            Log("Loaded favicon.ico");

            _directoryTemplate = File.ReadAllText("Directory.html"); //Load the directory template into memory.
            Log("Loaded Directory.html");

            _errorTemplate = File.ReadAllText("Error.html"); //Load the error templace into memory.
            Log("Loaded Error.html");
        }

        public RequestType GetRequestType(string urlPath)
        {
            var path = ToLocal(urlPath, _folderRoot); //Turn the url into a local file path.

            if (Path.GetFileName(path)?.Equals("favicon.ico") ?? false) //If the path's file name equals favicon.ico, return RequestType.Icon.
            {
                return RequestType.Icon;
            }
            if (File.Exists(path)) //Else if the selected file exists, return RequestType.File.
            {
                return RequestType.File;
            }
            if (Directory.Exists(path)) // Else if the selected directory exists, return RequestType.Directory
            {
                return RequestType.Directory;
            }
            return RequestType.Other; // If all else fails, just return RequestType.Other (Error)
        }

        #region Handlers

        public void HandleIcon(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var outputStream = response.OutputStream;

                response.KeepAlive = false;
                response.Headers.Add("Content-Type", "image/x-icon");
                response.Headers.Add("Content-Disposition", "inline; filename=\"favicon.ico\"");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.Headers.Add("Cache-Control", "public");
                response.Headers.Add("Expires", "access plus 1 day");

                if (request.Headers["Accept-Encoding"]?.Contains("gzip") ?? false) // If the request accepts gzip compression, send the compressed favicon.ico.
                {
                    response.Headers.Add("Content-Encoding", "gzip");
                    response.ContentLength64 = _compressedIconBytes.LongLength;
                    outputStream.Write(_compressedIconBytes, 0, _compressedIconBytes.Length);
                }
                else
                {
                    response.ContentLength64 = _iconBytes.LongLength;
                    outputStream.Write(_iconBytes, 0, _iconBytes.Length);
                }

                outputStream.Flush();
                response.Close();
            }
            catch (HttpListenerException)
            {
                context.Response.Abort();
            }
            catch (Exception e)
            {
                Log($"[Error] {e.Message}");
                context.Response.Abort();
            }
        }

        public void HandleFile(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var outputStream = response.OutputStream;

                var fileInfo = new FileInfo(ToLocal(request.Url.LocalPath, _folderRoot)); //Get info about the requested file.

                using (var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read)) //Open the file for reading, and allow other programs to also read the file
                {
                    var fileLength = fileStream.Length; // Get the length of the file.

                    var match = RangeRegex.Match(request.Headers["Range"] ?? string.Empty); // Get the Range header, 
                    var start = match.Groups["start"].Success ? long.Parse(match.Groups["start"].Value, NumberStyles.Integer) : 0L;
                    var finish = match.Groups["end"].Success ? long.Parse(match.Groups["end"].Value, NumberStyles.Integer) + 1 : fileLength;
                    var length = finish - start;

                    response.KeepAlive = false;
                    response.Headers.Add("Content-Type", MimeTypeMap.GetMimeType(fileInfo.Extension)); //Use MimeTypeMap to get the file's mime type.
                    response.Headers.Add("Content-Disposition", $"inline; filename={fileInfo.Name}");
                    response.Headers.Add("Date", $"{DateTime.Now:R}");
                    response.Headers.Add("Last-Modified", $"{fileInfo.LastWriteTime:R}");
                    response.Headers.Add("Accept-Ranges", "bytes");
                    response.Headers.Add("Content-Range", $"bytes {start}-{finish - 1}/{fileLength}");
                    response.ContentLength64 = length;
                    if (start >= 0 && finish <= fileLength) //If the requested range is not possible, return status code 416 (Requested Range not Satisfiable).
                    {
                        response.StatusCode = (start == 0 && finish == fileLength) ? 200 : 206; //If the range is the same as the file size, the status code is 200 (OK), else the status is 206 (Partial Content).

                        fileStream.Seek(start, SeekOrigin.Begin); //Start the filestream reading at the requested start.
                        var buffer = new byte[BufferSize]; //Make the buffer the same size as the buffer size constant.
                        for (var i = 0; i < length; i += BufferSize)
                        {
                            outputStream.Write(buffer, 0, fileStream.Read(buffer, 0, (int) Math.Min(length - i, BufferSize))); // Read then Write the maximum of the buffer size.
                        }
                    }
                    else
                    {
                        response.StatusCode = 416;
                    }
                }
                outputStream.Flush();
                response.Close();
            }
            catch (HttpListenerException)
            {
                context.Response.Abort();
            }
            catch (Exception e)
            {
                Log($"[Error] {e.Message}");
                context.Response.Abort();
            }
        }

        public void HandleDirectory(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var outputStream = response.OutputStream;

                var host = request.UserHostName;
                var url = request.Url.LocalPath;
                var directoryInfo = new DirectoryInfo(ToLocal(url, _folderRoot));

                response.KeepAlive = false;
                response.ContentType = "text/html";
                response.Headers.Add("Content-Type", "text/html; charset=UTF-8");
                response.Headers.Add("Content-Language", "en");
                response.Headers.Add("Content-Disposition", $"inline; filename={directoryInfo.Name}.html");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.Headers.Add("Last-Modified", $"{directoryInfo.LastWriteTime:R}");
                //response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate"); // Uncomment for no caching.
                //response.Headers.Add("Pragma", "no-cache"); // Uncomment for no caching.
                //response.Headers.Add("Expires", "0"); // Uncomment for no caching.
                response.StatusCode = 200;

                var sb = new StringBuilder();

                foreach (var directory in directoryInfo.EnumerateDirectories().OrderBy(s => s.Name))
                {
                    sb.AppendLine($@"<tr><td class=""name""><a href=""//{host}/{ToUrl(directory.FullName, _folderRoot)}/"">/{directory.Name}/</a></td><td class=""date"">{directory.LastWriteTime:G}</td><td class=""size"">{(_showFolderSize ? (directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(s => s.Length) / 1024) : 0)} KB</td><tr>");
                }
                foreach (var file in directoryInfo.EnumerateFiles().OrderBy(s => s.Name))
                {
                    sb.AppendLine($"<tr><td class=\"name\"><a href=\"//{host}/{ToUrl(file.FullName, _folderRoot)}\">/{file.Name}</a></td><td class=\"date\">{file.LastWriteTime:G}</td><td class=\"size\">{file.Length / 1024} KB</td></tr>");
                }

                var bytes = Encoding.UTF8.GetBytes(Replace(_directoryTemplate, directoryInfo.Name, $"Directory of {url}", $@"//{host}/{ToUrl(GetParent(directoryInfo, _folderRoot), _folderRoot)}", $@"//{host}/", sb)); //Fill the directory template with the required information, then encode it into UTF8 bytes.

                if (request.Headers["Accept-Encoding"]?.Contains("gzip") ?? false) //If the request accepts gzip compression, compress the bytes.
                {
                    response.Headers.Add("Content-Encoding", "gzip");
                    bytes = Compress(bytes);
                }

                response.ContentLength64 = bytes.LongLength;
                outputStream.Write(bytes, 0, bytes.Length);
                outputStream.Flush();
                response.Close();
            }
            catch (HttpListenerException)
            {
                context.Response.Abort();
            }
            catch (Exception e)
            {
                Log($"[Error] {e.Message}");
                context.Response.Abort();
            }
        }

        public void HandleOther(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var outputStream = response.OutputStream;

                response.KeepAlive = false;
                response.ContentType = "text/html";
                response.Headers.Add("Content-Type", "text/html; charset=UTF-8");
                response.Headers.Add("Content-Language", "en");
                response.Headers.Add("Content-Disposition", "inline; filename=Error.html");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.StatusCode = 200;

                var bytes = Encoding.UTF8.GetBytes(Replace(_errorTemplate, context.Request.Url.LocalPath)); //Fill in the error template with the url's local request, then encode it into UTF8 bytes.

                if (request.Headers["Accept-Encoding"]?.Contains("gzip") ?? false) //If the request accepts gzip compression, compress the bytes.
                {
                    response.Headers.Add("Content-Encoding", "gzip");
                    bytes = Compress(bytes);
                }

                response.ContentLength64 = bytes.LongLength;
                outputStream.Write(bytes, 0, bytes.Length);
                outputStream.Flush();
                response.Close();
            }
            catch (HttpListenerException)
            {
                context.Response.Abort();
            }
            catch (Exception e)
            {
                Log($"[Error] {e.Message}");
                context.Response.Abort();
            }
        }

        #endregion

        #region Statics

        private static readonly Regex RangeRegex = new Regex(@"bytes[= ](?<start>\d+)?-(?<end>\d+)?", RegexOptions.Compiled); //Regex for the request's Range header.

        private static string ToUrl(string local, string root)
        {
            local = local.Trim(); //Trim the input to be safe.
            root = root.Trim();

            if (local.StartsWith(root, StringComparison.OrdinalIgnoreCase)) //If the local request starts with the root folder, then get the substring after the root and return the HtmlEncoded version of it.
            {
                return HttpUtility.HtmlEncode(local.Substring(root.Length));
            }
            return string.Empty;
        }

        private static string ToLocal(string url, string root) { return Path.Combine(root, HttpUtility.HtmlDecode(url).Trim().TrimStart('/').Replace('/', '\\')); //Decode the url, trim it, remove any leading '/', replace the rest with '\', then combine it with the root folder. 
        }

        private static string GetParent(DirectoryInfo directory, string root)
        {
            var path = directory.FullName.Trim();

            if (path.Equals(root.Trim(), StringComparison.OrdinalIgnoreCase)) //If the directory's equals the root path, return the directory's path.
            {
                return path;
            }
            return directory.Parent?.FullName ?? path; //If the directory has no parent, return the directory's path.
        }

        private static string Replace(string input, params object[] parameters)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                input = input.Replace($"%{i}%", parameters[i].ToString());
            }
            return input;
        }

        private static void Log(object data)
        {
            Debug.WriteLine($"{DateTime.Now:R} | [Handler] {data}");
            Console.WriteLine($"{DateTime.Now:R} | [Handler] {data}");
        }

        private static byte[] Compress(byte[] raw)
        {
            var memoryStream = new MemoryStream(); //Create a stream in memory.

            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true)) //Using a gZipstream which compressed the given bytes to memory (the memoryStream is disposed when the gZipStream is disposed). 
            {
                gZipStream.Write(raw, 0, raw.Length); //Write the raw bites to the gZipStream.
            }
            return memoryStream.ToArray(); //Return the memoryStream as an arraw of bytes.
        }

        private static Stream GetResourceStream(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(GetResourceNames.OrderBy(s => s.Length).First(s => s.EndsWith(name))); //Return shortest resource name which ends with the input name.
            
        }

        private static IEnumerable<string> GetResourceNames
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetManifestResourceNames(); //Return a list of embeded resources.
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            return bytes;
        }

        private static void CreateFileFromResource(string name)
        {
            if (!File.Exists(name))
            {
                File.WriteAllBytes(name, ReadAllBytes(GetResourceStream(name)));
            }
        }

        #endregion
    }
}