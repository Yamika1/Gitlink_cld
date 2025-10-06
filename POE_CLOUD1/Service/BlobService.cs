using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace POE_CLOUD1.Service
{
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;

    public class BlobService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _containerClient;

        public BlobService(string connectionString, string containerName = "blobcontainer")
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            _containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            _containerClient.CreateIfNotExists(PublicAccessType.Blob); 
        }


        public async Task<string> UploadAsync(Stream fileStream, string fileName)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(fileStream, overwrite: true);
            return blobClient.Uri.ToString();
        }
        public async Task DeleteBlobAsync(string blobUri)
        {
            Uri uri = new Uri(blobUri);
            string blobName = uri.Segments[^1];
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
        }

        
        public async Task<List<string>> GetAllBlobsAsync()
        {
            var blobs = new List<string>();
            await foreach (var blob in _containerClient.GetBlobsAsync())
            {
                blobs.Add(_containerClient.GetBlobClient(blob.Name).Uri.ToString());
            }
            return blobs;
        }
    }
}