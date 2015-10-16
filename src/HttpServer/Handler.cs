using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
            Folder,
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

        private const int PacketSize = 4096;
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
            _iconBytes = File.ReadAllBytes("favicon.ico");
            _compressedIconBytes = Compress(_iconBytes);
            Log("Loaded favicon.ico");
            _directoryTemplate = File.ReadAllText("Directory.html");
            Log("Loaded Directory.html");
            _errorTemplate = File.ReadAllText("Error.html");
            Log("Loaded Error.html");
        }

        public RequestType GetRequestType(string urlPath)
        {
            var path = ToLocal(urlPath, _folderRoot);
            if ((Path.GetFileName(path) ?? string.Empty) == "favicon.ico")
            {
                return RequestType.Icon;
            }
            if (File.Exists(path))
            {
                return RequestType.File;
            }
            if (Directory.Exists(path))
            {
                return RequestType.Folder;
            }
            return RequestType.Other;
        }

        public void HandleIcon(HttpListenerContext context)
        {
            try
            {
                if (!File.Exists("favicon.ico"))
                {
                    throw new FileNotFoundException("favicon");
                }

                var request = context.Request;
                var response = context.Response;
                var outputStream = response.OutputStream;

                response.KeepAlive = false;
                response.Headers.Add("Content-Type", "image/x-icon");
                response.Headers.Add("Content-Disposition", "inline; filename=\"favicon.ico\"");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.Headers.Add("Cache-Control", "public");
                response.Headers.Add("Expires", "access plus 1 day");

                if ((request.Headers["Accept-Encoding"] ?? string.Empty).Contains("gzip"))
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
                var fileInfo = new FileInfo(ToLocal(request.Url.LocalPath, _folderRoot));
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException($"{fileInfo.FullName} not found.");
                }
                var response = context.Response;
                var outputStream = response.OutputStream;

                using (var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var fileLength = fileStream.Length;

                    var match = RangeRegex.Match(request.Headers["Range"] ?? string.Empty);
                    var start = match.Groups["start"].Success ? long.Parse(match.Groups["start"].Value, NumberStyles.Integer) : 0L;
                    var finish = match.Groups["end"].Success ? long.Parse(match.Groups["end"].Value, NumberStyles.Integer) + 1 : fileLength;

                    response.KeepAlive = false;
                    response.Headers.Add("Content-Type", MimeTypeMap.GetMimeType(fileInfo.Extension));
                    // Ask the system for the filetype.
                    response.Headers.Add("Content-Disposition", $"inline; filename={fileInfo.Name}");
                    response.Headers.Add("Date", $"{DateTime.Now:R}");
                    response.Headers.Add("Last-Modified", $"{fileInfo.LastWriteTime:R}");
                    response.Headers.Add("Accept-Ranges", "bytes");
                    response.Headers.Add("Content-Range", $"bytes {start}-{finish - 1}/{fileLength}");
                    response.ContentLength64 = finish - start;
                    if (start >= 0 && finish <= fileLength)
                    {
                        response.StatusCode = (start == 0 && finish == fileLength) ? 200 : 206;

                        fileStream.Seek(start, SeekOrigin.Begin);
                        var buffer = new byte[PacketSize];
                        for (var i = 0; i < finish - start; i += PacketSize)
                        {
                            outputStream.Write(buffer, 0, (int) Math.Min(finish - start - i, fileStream.Read(buffer, 0, PacketSize)));
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
                var url = request.Url.LocalPath;
                var directoryInfo = new DirectoryInfo(ToLocal(url, _folderRoot));

                if (!directoryInfo.Exists)
                {
                    throw new DirectoryNotFoundException($"{directoryInfo.FullName} not found.");
                }

                var response = context.Response;
                var host = request.UserHostName;
                var outputStream = response.OutputStream;

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

                var bytes = Encoding.UTF8.GetBytes(Replace(_directoryTemplate, directoryInfo.Name, $"Directory of {url}", $@"//{host}/{ToUrl(GetParent(directoryInfo, _folderRoot), _folderRoot)}", $@"//{host}/", sb));

                if ((request.Headers["Accept-Encoding"] ?? string.Empty).Contains("gzip"))
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

                var bytes = Encoding.UTF8.GetBytes(Replace(_errorTemplate, context.Request.Url.LocalPath));

                if ((request.Headers["Accept-Encoding"] ?? string.Empty).Contains("gzip"))
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

        #region Statics

        private static readonly Regex RangeRegex = new Regex(@"bytes=?(?<start>\d+)?-(?<end>\d+)?", RegexOptions.Compiled);

        private static string ToUrl(string local, string root)
        {
            local = local.Trim();
            root = root.Trim();

            return local.Length < root.Length ? string.Empty : (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? string.Empty : (local.Equals(root, StringComparison.OrdinalIgnoreCase) ? string.Empty : HttpUtility.HtmlEncode(local.Substring(root.Length))));
        }

        private static string ToLocal(string url, string root)
        {
            url = HttpUtility.HtmlDecode(url).Trim().TrimStart('/', '\\').Replace('/', '\\');
            return Path.Combine(root, url);
        }

        private static string GetParent(DirectoryInfo directory, string root)
        {
            var path = directory.FullName.Trim();
            root = root.Trim();

            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return directory.Parent?.FullName ?? path;
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
            var memoryStream = new MemoryStream();
            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
            {
                gZipStream.Write(raw, 0, raw.Length);
            }
            return memoryStream.ToArray();
        }

        #endregion
    }
}