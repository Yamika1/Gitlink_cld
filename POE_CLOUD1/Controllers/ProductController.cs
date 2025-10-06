using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using POE_CLOUD1.Models;
using POE_CLOUD1.Service;
using System.Text.Json;

namespace POE_CLOUD1.Controllers
{
    public class ProductController : Controller
    {
        private readonly TableStorageService _tableStorageService;
        private readonly QueueService _svc;
        private readonly AzureFileShareService _fileShareService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        private readonly string _connectionString;
        private readonly string _containerName;

        public ProductController(
            TableStorageService tableStorageService,
            AzureFileShareService fileShareService,
            QueueService svc,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _tableStorageService = tableStorageService;
            _fileShareService = fileShareService;
            _svc = svc;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;

            _connectionString = _configuration.GetConnectionString("AzureStorage");
            _containerName = _configuration["BlobStorage:Container"];
        }

        // ========================= INDEX =========================
        public async Task<IActionResult> Index()
        {
            IEnumerable<Product> products = new List<Product>();
            var httpClient = _httpClientFactory.CreateClient();
            var apiBaseUrl = _configuration["FunctionApi:BaseUrl"];

            try
            {
                var httpResponseMessage = await httpClient.GetAsync($"{apiBaseUrl}product");
                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    using var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    products = await JsonSerializer.DeserializeAsync<IEnumerable<Product>>(contentStream, options);
                }
                else
                {
                    ViewBag.ErrorMessage = "API returned an error while retrieving products.";
                }
            }
            catch
            {
                ViewBag.ErrorMessage = "Could not connect to the API. Please ensure the Azure Function is running.";
            }

            try { ViewBag.LocalFiles = await _fileShareService.ListFilesAsync("uploads"); }
            catch { ViewBag.LocalFiles = new List<FileModel>(); }

            try { ViewBag.BlobFiles = await FetchBlobUrlsAsync(); }
            catch { ViewBag.BlobFiles = new List<string>(); }

            try { ViewBag.QueueMessages = await _svc.PeekMessagesAsync(5); }
            catch { ViewBag.QueueMessages = new List<string>(); }

            return View(products);
        }

        // ========================= BLOB METHODS =========================
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile uploadedFile)
        {
            if (uploadedFile != null && uploadedFile.Length > 0)
            {
                await UploadFileToBlobStorageAsync(uploadedFile);
                TempData["message"] = $"File '{uploadedFile.FileName}' uploaded to Blob successfully!";
            }
            else
            {
                TempData["message"] = "Please select a file to upload.";
            }

            return RedirectToAction("Index");
        }

        private async Task UploadFileToBlobStorageAsync(IFormFile uploadedFile)
        {
            var containerClient = new BlobContainerClient(_connectionString, _containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(uploadedFile.FileName);
            using var stream = uploadedFile.OpenReadStream();
            await blobClient.UploadAsync(stream, overwrite: true);
        }

        private async Task<List<string>> FetchBlobUrlsAsync()
        {
            var blobUrls = new List<string>();
            var containerClient = new BlobContainerClient(_connectionString, _containerName);

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                blobUrls.Add(blobClient.Uri.ToString());
            }
            return blobUrls;
        }

        private async Task<string> UploadFileToBlobStorageAndReturnUrl(Stream stream, string fileName)
        {
            var containerClient = new BlobContainerClient(_connectionString, _containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(stream, overwrite: true);
            return blobClient.Uri.ToString();
        }

        private async Task DeleteBlobAsync(string blobUrl)
        {
            var containerClient = new BlobContainerClient(_connectionString, _containerName);
            var blobName = Path.GetFileName(blobUrl);
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }

        // ========================= PRODUCT CRUD =========================
        [HttpGet]
        public IActionResult AddProduct() => View();

        [HttpPost]
        public async Task<IActionResult> AddProduct(Product product, IFormFile? file)
        {
            if (file != null && file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                product.ImageURL = await UploadFileToBlobStorageAndReturnUrl(stream, file.FileName);
            }

            if (ModelState.IsValid)
            {
                product.PartitionKey = "ProductPartition";
                product.RowKey = Guid.NewGuid().ToString();
                await _tableStorageService.AddProductAsync(product);
                TempData["message"] = "Product added successfully!";
                return RedirectToAction("Index");
            }

            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> EditProduct(string rowKey)
        {
            var product = await _tableStorageService.GetProductByIdAsync("ProductPartition", rowKey);
            if (product == null) return NotFound();
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProduct(Product product, IFormFile? file)
        {
            if (!ModelState.IsValid)
                return View(product);

            try
            {
                if (file != null && file.Length > 0)
                {
                    if (!string.IsNullOrEmpty(product.ImageURL))
                    {
                        await DeleteBlobAsync(product.ImageURL);
                    }

                    using var stream = file.OpenReadStream();
                    product.ImageURL = await UploadFileToBlobStorageAndReturnUrl(stream, file.FileName);
                }

                await _tableStorageService.UpdateProductAsync(product);
                TempData["message"] = "Product updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["message"] = $"Error updating product: {ex.Message}";
                return View(product);
            }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteProduct(string rowKey)
        {
            var product = await _tableStorageService.GetProductByIdAsync("ProductPartition", rowKey);
            if (product == null) return NotFound();
            return View(product);
        }

        [HttpPost, ActionName("DeleteProduct")]
        public async Task<IActionResult> DeleteConfirmed(string rowKey)
        {
            var product = await _tableStorageService.GetProductByIdAsync("ProductPartition", rowKey);
            if (product != null)
            {
                if (!string.IsNullOrEmpty(product.ImageURL))
                {
                    await DeleteBlobAsync(product.ImageURL);
                }

                await _tableStorageService.DeleteProductAsync("ProductPartition", rowKey);
                TempData["message"] = "Product deleted successfully!";
            }

            return RedirectToAction("Index");
        }

        // ========================= FILE SHARE =========================
        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("file", "Please select a file to upload");
                return await Index();
            }

            try
            {
                using var stream = file.OpenReadStream();
                await _fileShareService.UploadFileAsync("uploads", file.FileName, stream);
                TempData["message"] = $"File '{file.FileName}' uploaded successfully";
            }
            catch (Exception e)
            {
                TempData["message"] = $"File upload failed: {e.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return BadRequest("File name cannot be null or empty");

            try
            {
                var fileStream = await _fileShareService.DownloadFileAsync("uploads", fileName);
                if (fileStream == null) return NotFound($"File '{fileName}' not found");

                return File(fileStream, "application/octet-stream", fileName);
            }
            catch (Exception e)
            {
                return BadRequest($"Error downloading file: {e.Message}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(string? message)
        {

            if (string.IsNullOrWhiteSpace(message))
            {
                ViewBag.Msg = "Please enter a message before sending.";
            }
            else
            {
                await _svc.SendAsync(message.Trim());
                ViewBag.Msg = $"Message sent: \"{message}\"";
            }


            try
            {
                ViewBag.QueueMessages = await _svc.PeekMessagesAsync(5);
            }
            catch
            {
                ViewBag.QueueMessages = new List<string>();
            }

            return View("Index");
        }
    }
}