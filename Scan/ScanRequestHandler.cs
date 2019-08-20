using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using MS.Ess.Scan.DataContracts.Private;
using Octokit;
using OctokitDemo.Scan.Helpers;

namespace OctokitDemo
{
    public class ScanRequestHandler
    {
        public ScanRequestHandler()
        {
        }

        public Task Start(GitHubClient client, long repoId, Guid requestId)
        {
            return this.StartAsync(client, repoId, requestId);
        }

        private async Task StartAsync(GitHubClient client, long repoId, Guid requestId)
        {
            // download a ZIP
            byte[] buffer = null;
            try
            {
                buffer = await ScanHelper.DownloadRepoZip(client, repoId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            // upload buffer to storage
            string blobUri = await ScanHelper.UploadBufferToStorage(buffer, requestId.ToString());

            // create worker notification message (include the requestId)
            MalwareDeterminationRequest scanRequest = new MalwareDeterminationRequest
            {
                ClientId = "GitHubScanX",
                FileName = $"{requestId.ToString()}.zip",
                FileSizeInBytes = 1000, //dummy
                RequestId = requestId,
                Uri = new Uri(blobUri)
            };

            // notify worker (aka put the notification message to a queue
            ScanXMock mock = new ScanXMock();

            // Fire and forget (for POC)
            mock.SendScanRequest(scanRequest);
        }

        public async Task<bool> TryGetScanResult(string requestId)
        {
            // Try to get a result of scan request (by the requestId) from a notification service bus queue
            ScanXMock mock = new ScanXMock();
            return await mock.TryGetResult(Guid.Parse(requestId));
       }

        public async Task<MalwareDeterminationResult> GetScanResult(string requestId)
        {
            // Try to get a result of scan request (by the requestId) from a notification service bus queue
            ScanXMock mock = new ScanXMock();
            return await mock.GetResult(Guid.Parse(requestId));
       }
    }
}