using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using Google.Apis.Download;
using UnityEngine;

namespace NanoProfiler.Editor
{
    public class GoogleCloudService
    {
        private const string BUCKET_NAME = "perfetto-data"; // Configure your bucket here
        private const string CACHE_DIR = "perfetto-traces"; // Configure your bucket here
        private readonly StorageClient _storageClient;
        private readonly string _cacheDirectory;

        public GoogleCloudService()
        {
            // Assuming GOOGLE_APPLICATION_CREDENTIALS is set in env or user is authenticated via gcloud
            try 
            {
                _storageClient = StorageClient.Create();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize GCS Client. Ensure credentials are set. Error: {e.Message}\n{e.StackTrace}");
            }

            _cacheDirectory = Path.Combine(Application.temporaryCachePath, CACHE_DIR);
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);
        }

        public async Task<List<string>> ListVersionsAsync()
        {
            if (_storageClient == null)
                return new List<string>();

            var versions = new List<string>();
            try
            {
                var options = new ListObjectsOptions { Delimiter = "/" };
                var objects = _storageClient.ListObjectsAsync(BUCKET_NAME, "", options);
                await foreach (var obj in objects.AsRawResponses())
                {
                    if (obj.Prefixes == null)
                        continue;
                    
                    versions.AddRange(obj.Prefixes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error listing versions: {ex.Message}\n{ex.StackTrace}");
            }

            return versions.OrderByDescending(v => v).ToList();
        }

        public async Task<List<string>> ListModelsAsync(string remotePrefix)
        {
            if (_storageClient == null)
                return new List<string>();

            var models = new List<string>();
            try
            {
                var options = new ListObjectsOptions { Delimiter = "/" };
                var objects = _storageClient.ListObjectsAsync(BUCKET_NAME, remotePrefix, options);
                await foreach (var obj in objects.AsRawResponses())
                {
                    if (obj.Prefixes == null)
                        continue;

                    models.AddRange(obj.Prefixes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error listing models for {remotePrefix}: {ex.Message}\n{ex.StackTrace}");
            }

            return models.OrderBy(m => m).ToList();
        }

        public record struct TraceInfo(string RemotePath, ulong SizeBytes);
        
        public async Task<List<TraceInfo>> ListTracesAsync(string remotePrefix)
        {
            if (_storageClient == null)
                return new List<TraceInfo>();

            var traces = new List<TraceInfo>();
            try
            {
                var objects = _storageClient.ListObjectsAsync(BUCKET_NAME, remotePrefix);
                
                await foreach (var obj in objects)
                {
                    if (!obj.Name.EndsWith(".trace"))
                        continue;
                    
                    traces.Add(new TraceInfo(obj.Name, obj.Size ?? 0));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error listing traces for {remotePrefix}: {ex.Message}\n{ex.StackTrace}");
            }

            return traces.OrderByDescending(t => t.RemotePath).ToList();
        }
        
        /// <summary>
        /// * -> Matches any character except '/'
        /// ** -> Matches any character including '/'
        /// Example: "perfetto-data/*/iPhone128/**" - matches all traces for iPhone128 across all versions
        /// </summary>
        public async Task<List<TraceInfo>> FindTraceAsync(string pattern)
        {
            var options = new ListObjectsOptions
            {
                MatchGlob = pattern,
                Fields = "items(name,size)" // Optimization: Fetch only the names to save bandwidth
            };
            var objects = _storageClient.ListObjectsAsync(BUCKET_NAME, "", options);

            var traces = new HashSet<TraceInfo>();
            await foreach (var obj in objects)
            {
                if (!obj.Name.EndsWith(".trace"))
                    continue;
                    
                traces.Add(new TraceInfo(obj.Name, obj.Size ?? 0));
            }
            
            return traces.OrderByDescending(t => t.RemotePath).ToList();
        }        
        
        public string GetTraceLocalPath(string remotePath)
        {
            var localFileName = remotePath.Replace("\\", "-").Replace("/", "-");
            return Path.Combine(_cacheDirectory, localFileName);
        }
        
        public bool IsTraceDownloaded(string remotePath)
        {
            var localPath = GetTraceLocalPath(remotePath);
            return File.Exists(localPath);
        }

        public async ValueTask<string> DownloadTraceAsync(string remotePath, Action<float> onProgress = null)
        {
            if (_storageClient == null)
                return null;

            var localPath = string.Empty;
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                    Directory.CreateDirectory(_cacheDirectory!);

                localPath = GetTraceLocalPath(remotePath);
                await using var fileStream = File.OpenWrite(localPath);
                
                var progress = new Progress<IDownloadProgress>(p => onProgress?.Invoke(p.BytesDownloaded));
                await _storageClient.DownloadObjectAsync(BUCKET_NAME, remotePath, fileStream, null, CancellationToken.None, progress);

                return localPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to download {remotePath}: {ex.Message}\n{ex.StackTrace}");
                
                // Clean up partial download
                if (File.Exists(localPath))
                    File.Delete(localPath);
            }
            
            return null;
        }
    }
}
