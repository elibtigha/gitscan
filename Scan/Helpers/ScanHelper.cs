using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Octokit;

namespace OctokitDemo.Scan.Helpers
{
    public class ScanHelper
    {
        public static async Task<byte[]> DownloadRepoZip(IGitHubClient client, long repoId, string gitRef = "")
        {
            return await client.Repository.Content.GetArchive(
                    repoId,
                    ArchiveFormat.Zipball,
                    gitRef)
                    .ConfigureAwait(false);
        }

        public static async Task<string> UploadBufferToStorage(byte[] buffer, string blobName)
        {
            try
            {
                string storageConnectionString = ConfigurationManager.AppSettings["StorageConnectionStringReadWrite"];

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);
                CloudBlobClient serviceClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = serviceClient.GetContainerReference("scanfiles");
                container.CreateIfNotExists();
                CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
                using (var stream = new MemoryStream(buffer, writable: false))
                {
                    await blob.UploadFromStreamAsync(stream);

                }

                //Create an ad-hoc Shared Access Policy with read permissions which will expire in 12 hours
                SharedAccessBlobPolicy policy = new SharedAccessBlobPolicy()
                {
                    Permissions = SharedAccessBlobPermissions.Read,
                    SharedAccessExpiryTime = DateTime.UtcNow.AddHours(12),
                };

                //Set content-disposition header for force download
                SharedAccessBlobHeaders headers = new SharedAccessBlobHeaders()
                {
                    ContentDisposition = $"attachment;filename=\"{blobName}.zip\"",
                };
                var sasToken = blob.GetSharedAccessSignature(policy, headers);

                //// For test only
                //WebClient webClient = new WebClient();
                //byte[] response = await webClient.DownloadDataTaskAsync(blob.Uri.AbsoluteUri + sasToken);
                //webClient.Dispose();

                return blob.Uri.AbsoluteUri + sasToken;
            }
            catch (Exception)
            {
                return null;
            }
        }

    }
}