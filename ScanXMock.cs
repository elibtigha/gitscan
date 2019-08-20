using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MS.Ess.Scan.DataContracts.Private;

namespace OctokitDemo
{
    public class ScanXMock
    {
        private static readonly Lazy<ConcurrentDictionary<Guid, MalwareDeterminationResult>> ScanResults =
            new Lazy<ConcurrentDictionary<Guid, MalwareDeterminationResult>>(
                () =>
                {
                    return new ConcurrentDictionary<Guid, MalwareDeterminationResult>();
                });


        public Task SendScanRequest(MalwareDeterminationRequest request)
        {
            // "Fire and forget" (for demo only)
            return Process(request);
;        }

        public async Task<MalwareDeterminationResult> GetResult(Guid requestId)
        {
            await Task.Delay(500);
            ScanResults.Value.TryRemove(requestId, out MalwareDeterminationResult result);
            return result;
        }

        public async Task<bool> TryGetResult(Guid requestId)
        {
            await Task.Delay(500);
            return ScanResults.Value.TryGetValue(requestId, out MalwareDeterminationResult result);
        }

        private async Task Process(MalwareDeterminationRequest request)
        {
            // Download ZIP
            WebClient webClient = new WebClient();
            byte[] response = await webClient.DownloadDataTaskAsync(request.Uri).ConfigureAwait(false);
            webClient.Dispose();

            // Unzip and try to find "malware" files
            List<ConfirmedMalwareInfo> detectedFiles = new List<ConfirmedMalwareInfo>();
            int unzippedFilesCount = 0;
            Stream unzippedEntryStream; // Unzipped data from a file in the archive
            using (Stream data = new MemoryStream(response))
            using (ZipArchive archive = new ZipArchive(data))
            {
                unzippedFilesCount = archive.Entries.Count;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.Contains("malware"))
                    {
                        using (unzippedEntryStream = entry.Open())
                        {
                            // convert stream to string
                            using (StreamReader reader = new StreamReader(unzippedEntryStream))
                            {
                                string text = await reader.ReadToEndAsync().ConfigureAwait(false);
                                // Expected value: <malwarename>:<avengine#1>,...,<avengine#n>
                                string[] arr = text.Split(new char[] { ':' });
                                ConfirmedMalwareInfo info = new ConfirmedMalwareInfo();
                                info.FileName = entry.FullName.Substring(entry.FullName.IndexOf('/'));
                                info.MalwareInfo = arr[0];
                                if (arr.Count() > 1)
                                {
                                    info.AvEngines = arr[1].Split(new char[] { ',' }).ToList();
                                }

                                detectedFiles.Add(info);
                            }
                        }
                    }
                }

                // Compose a scan result
                MalwareDeterminationResult detectionResult = new MalwareDeterminationResult();
                if (detectedFiles.Count() > 0)
                {
                    detectionResult.ClientId = request.ClientId;
                    detectionResult.RequestId = request.RequestId;
                    detectionResult.ConfirmedMalwares = detectedFiles;
                    detectionResult.WorkStatus = WorkStatus.ConfirmedMalware;
                }
                else
                {
                    detectionResult.WorkStatus = WorkStatus.Clean;
                }

                ScanResults.Value.TryAdd(request.RequestId, detectionResult);
            }
        }
    }
}