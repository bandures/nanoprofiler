using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace NanoProfiler.Editor
{
    public class PerfettoHost : IDisposable
    {
        private readonly string _uiRootPath;
        private readonly int _port;
        private HttpListener _listener;
        private Thread _serverThread;
        private volatile bool _isRunning;

        // Maps a virtual path (e.g. "current_trace.pftrace") to a physical disk path
        private readonly Dictionary<string, string> _virtualFileMap = new();

        /// <summary>
        /// Creates the host.
        /// </summary>
        /// <param name="uiFolderPath">The folder containing index.html, trace_processor.wasm, etc.</param>
        /// <param name="port">Port to listen on (default 9001)</param>
        public PerfettoHost(string uiFolderPath, int port = 9001)
        {
            if (!Directory.Exists(uiFolderPath))
                throw new DirectoryNotFoundException($"Perfetto UI folder not found at: {uiFolderPath}");

            _uiRootPath = uiFolderPath;
            _port = port;
        }

        public void Start()
        {
            if (_isRunning) return;

            _serverThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = "PerfettoHost_ServerThread"
            };
            _serverThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
        }

        public void Dispose()
        {
            Stop();
        }

        public void OpenTrace(string traceFilePath)
        {
            if (!_isRunning)
                Start();

            if (!File.Exists(traceFilePath))
            {
                Debug.LogError($"Trace file missing: {traceFilePath}");
                return;
            }

            // Map the file to a virtual URL path
            const string virtualFileName = "live_trace.pftrace";
            lock (_virtualFileMap)
            {
                _virtualFileMap[virtualFileName] = traceFilePath;
            }

            // Construct the URL that tells Perfetto to fetch the file from us
            var traceUrl = $"http://localhost:{_port}/{virtualFileName}";
            var uiUrl = $"http://localhost:{_port}/index.html#!/?url={traceUrl}";

            Debug.Log($"[Perfetto] Loading trace: {uiUrl}");

            Application.OpenURL(uiUrl);
        }

        private void Listen()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
                _isRunning = true;
                Debug.Log($"Server running at http://localhost:{_port}/");

                while (_isRunning && _listener.IsListening)
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
                }
            }
            catch (HttpListenerException)
            {
                /* Server stopped */
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var response = context.Response;
            
            var relativePath = context.Request.Url.LocalPath.TrimStart('/');
            if (string.IsNullOrEmpty(relativePath))
                relativePath = "index.html";

            try
            {
                // Required for SharedArrayBuffer (WASM multithreading)
                response.AddHeader("Cross-Origin-Opener-Policy", "same-origin");
                response.AddHeader("Cross-Origin-Embedder-Policy", "require-corp");
                response.AddHeader("Access-Control-Allow-Origin", "*");

                string filePathToServe = null;

                // Check if this request matches our virtual trace file
                lock (_virtualFileMap)
                {
                    if (_virtualFileMap.ContainsKey(relativePath))
                    {
                        filePathToServe = _virtualFileMap[relativePath];
                    }
                }

                // If not virtual, look in the UI folder
                if (filePathToServe == null)
                {
                    filePathToServe = Path.Combine(_uiRootPath, relativePath);

                    // Security: Prevent directory traversal
                    if (!Path.GetFullPath(filePathToServe).StartsWith(Path.GetFullPath(_uiRootPath), StringComparison.OrdinalIgnoreCase))
                    {
                        response.StatusCode = (int)HttpStatusCode.Forbidden;
                        response.Close();
                        return;
                    }
                }

                // Serve the file
                if (File.Exists(filePathToServe))
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentType = GetMimeType(filePathToServe);
                    response.ContentLength64 = new FileInfo(filePathToServe).Length;

                    using var fs = File.OpenRead(filePathToServe);
                    fs.CopyTo(response.OutputStream);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Error] Serving {relativePath}: {ex.Message}\n{ex.StackTrace}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                response.Close();
            }
        }

        private static string GetMimeType(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".html" => "text/html",
                ".js" => "application/javascript",
                ".wasm" => "application/wasm",
                ".css" => "text/css",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".pftrace" => "application/octet-stream",
                ".trace" => "application/octet-stream",
                ".gz" => "application/gzip",
                _ => "application/octet-stream",
            };
        }
    }
}