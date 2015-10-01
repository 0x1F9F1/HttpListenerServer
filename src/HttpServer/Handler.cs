using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeTypes;

namespace HttpListenerServer
{
    partial class Handler
    {
        private const int PacketSize = 16777216;
        private static readonly Regex RangeRegex = new Regex(@"bytes=?(?<start>\d+)?-(?<end>\d+)?", RegexOptions.Compiled);

        private readonly byte[] IconBytes;
        private readonly string FolderRoot;
        private readonly string DirectoryTemplate;
        private readonly string ErrorTemplate;

        public Handler(string root)
        {
            FolderRoot = root;
            IconBytes = File.ReadAllBytes("favicon.ico");
            DirectoryTemplate = File.ReadAllText("Directory.html");
            ErrorTemplate = File.ReadAllText("Error.html");
        }
        public bool HandleIcon(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;

                if (!Path.GetFileName(request.Url.AbsolutePath).Equals("favicon.ico")) return false;

                var response = context.Response;
                var outputStream = response.OutputStream;

                response.KeepAlive = false;
                response.Headers.Add("Content-Type", "image/x-icon");
                response.Headers.Add("Content-Disposition", "inline; filename=\"favicon.ico\"");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.Headers.Add("Cache-Control", "public");
                response.Headers.Add("Expires", "access plus 1 day");
                response.ContentLength64 = IconBytes.LongLength;
                
                outputStream.Write(IconBytes, 0, IconBytes.Length);
                outputStream.Flush();
                response.Close();

                return true;
            }
            catch (Exception e)
            {
                Log(e.Message);
                return false;
            }
        }

        public bool HandleFile(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var fileInfo = new FileInfo(ToLocal(request.Url.AbsolutePath, FolderRoot));
                if (!fileInfo.Exists) return false;

                var response = context.Response;
                var outputStream = response.OutputStream;

                using (var fileStream = fileInfo.OpenRead())
                {
                    var fileLength = fileStream.Length;
                    var mimeType = MimeTypeMap.GetMimeType(fileInfo.Extension);

                    var range = request.Headers["Range"] ?? string.Empty;
                    var match = RangeRegex.Match(range);
                    var start = match.Groups["start"].Success ? long.Parse(match.Groups["start"].Value) : 0L;
                    var finish = match.Groups["end"].Success ? long.Parse(match.Groups["end"].Value) : fileLength;

                    response.KeepAlive = false;
                    response.Headers.Add("Content-Type", mimeType); // Ask the system for the filetype.
                    response.Headers.Add("Content-Disposition", $"inline; filename={fileInfo.Name}");
                    response.Headers.Add("Date", $"{DateTime.Now:R}");
                    response.Headers.Add("Last-Modified", $"{fileInfo.LastWriteTime:R}");
                    response.Headers.Add("Accept-Ranges", "bytes");
                    response.Headers.Add("Content-Range", $"bytes {start}-{finish - 1}/{fileLength}");
                    response.ContentLength64 = finish - start;
                    response.StatusCode = (start == 0 && finish == fileLength) ? 200 : 206;

                    fileStream.Seek(start, SeekOrigin.Begin);
                    for (var i = 0; i < finish - start; i += PacketSize)
                    {
                        var buffer = new byte[PacketSize];
                        var bytes = fileStream.Read(buffer, 0, PacketSize);
                        var bytesToWrite = (int)Math.Min(finish - start - i, bytes);

                        outputStream.Write(buffer, 0, bytesToWrite);
                    }
                }

                outputStream.Flush();
                response.Close();

                return true;
            }
            catch (Exception e)
            {
                Log(e.Message);
                return false;
            }
        }

        public bool HandleDirectory(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var directoryInfo = new DirectoryInfo(ToLocal(request.Url.AbsolutePath, FolderRoot));

                if (!directoryInfo.Exists) return false;

                var response = context.Response;
                var outputStream = response.OutputStream;
                var url = request.Url.AbsolutePath;
                var host = request.UserHostName;

                response.KeepAlive = false;
                response.ContentType = "text/html";
                response.Headers.Add("Content-Type", "text/html; charset=UTF-8");
                response.Headers.Add("Content-Language", "en");
                response.Headers.Add("Content-Disposition", $"inline; filename={directoryInfo.Name}.html");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.Headers.Add("Last-Modified", $"{directoryInfo.LastWriteTime:R}");
                response.StatusCode = 200;

                var sb = new StringBuilder();
                foreach (var directory in directoryInfo.EnumerateDirectories().OrderBy(s => s.Name))
                {
                    sb.AppendLine(
                        $"<tr><td class=\"name\"><a href=\"//{host}/{ToUrl(directory.FullName, FolderRoot)}/\">/{directory.Name}/</a></td><td class=\"date\">{directory.LastWriteTime:G}</td><td class=\"size\">{directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(s => s.Length) / 1024} KB</td><tr>");
                }
                foreach (var file in directoryInfo.EnumerateFiles().OrderBy(s => s.Name))
                {
                    sb.AppendLine(
                        $"<tr><td class=\"name\"><a href=\"//{host}/{ToUrl(file.FullName, FolderRoot)}\">/{file.Name}</a></td><td class=\"date\">{file.LastWriteTime:G}</td><td class=\"size\">{file.Length / 1024} KB</td></tr>");
                }

                var bytes = Encoding.UTF8.GetBytes(Replace(DirectoryTemplate, directoryInfo.Name, $"Directory of {url}", $"//http://{host}/{GetParent(directoryInfo, FolderRoot)}", $"//http://{host}/", sb));

                response.ContentLength64 = bytes.LongLength;

                outputStream.Write(bytes, 0, bytes.Length);
                outputStream.Flush();
                response.Close();

                return true;

            }
            catch (Exception e) 
            {
                Log(e.Message);
                return false;
            }

        }

        public bool HandleOther(HttpListenerContext context)
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
                response.Headers.Add("Content-Disposition", $"inline; filename=Error.html");
                response.Headers.Add("Date", $"{DateTime.Now:R}");
                response.StatusCode = 200;

                var bytes = Encoding.UTF8.GetBytes(Replace(ErrorTemplate, request.Url.LocalPath));

                response.ContentLength64 = bytes.LongLength;

                outputStream.Write(bytes, 0, bytes.Length);
                outputStream.Flush();
                response.Close();

                return true;
            }
            catch (Exception e)
            {
                Log(e.Message);
                return false;
            }
        }
    }
}