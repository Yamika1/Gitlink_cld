using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using POE_CLOUD1.Models;
using POE_CLOUD1.Service;
using System.Text.Json;

namespace POE_CLOUD1.Controllers
{

    public class OrderController : Controller
    {
        private readonly TableStorageService _tableStorageService;
        private readonly QueueService _svc;
        private readonly AzureFileShareService _fileShareService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        private readonly string _connectionString;
        private readonly string _containerName;

        public OrderController(
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

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            IEnumerable<Order> orders = new List<Order>();

            try { orders = await _tableStorageService.GetAllOrdersAsync("OrderPartition"); }
            catch { ViewBag.ErrorMessage = "Could not retrieve orders from Table Storage."; }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var apiBaseUrl = _configuration["FunctionApi:BaseUrl"];
                var response = await httpClient.GetAsync($"{apiBaseUrl}order");

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var apiOrders = await JsonSerializer.DeserializeAsync<IEnumerable<Order>>(stream, options);
                    if (apiOrders != null) orders = orders.Concat(apiOrders);
                }
                else
                {
                    ViewBag.ErrorMessage = "API returned an error while retrieving orders.";
                }
            }
            catch { ViewBag.ErrorMessage ??= "Could not connect to the API."; }

            try { ViewBag.LocalFiles = await _fileShareService.ListFilesAsync("uploads"); }
            catch { ViewBag.LocalFiles = new List<FileModel>(); }

            try { ViewBag.BlobFiles = await FetchBlobUrlsAsync(); }
            catch { ViewBag.BlobFiles = new List<string>(); }

            try { ViewBag.QueueMessages = await _svc.PeekMessagesAsync(5); }
            catch { ViewBag.QueueMessages = new List<string>(); }

            return View(orders);
        }

        [HttpGet]
        public IActionResult AddOrder() => View(new Order());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddOrder(Order order, IFormFile? file)
        {
            if (file != null && file.Length > 0)
                order.ImageUrl = await UploadFileToBlobStorageAndReturnUrl(file.OpenReadStream(), file.FileName);

            if (ModelState.IsValid)
            {
                order.PartitionKey = "OrderPartition";
                order.RowKey = Guid.NewGuid().ToString();
                order.Timestamp = DateTimeOffset.UtcNow;
                order.OrderDate = DateTime.UtcNow;

                await _tableStorageService.AddOrdersAsync(order);

                TempData["message"] = "Order added successfully!";
                return RedirectToAction("Index");
            }

            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
                return NotFound();

            var order = await _tableStorageService.GetOrderAsync(partitionKey, rowKey);
            if (order == null) return NotFound();

            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
                return NotFound();

            var order = await _tableStorageService.GetOrderAsync(partitionKey, rowKey);
            if (order == null) return NotFound();

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Order order, IFormFile? file)
        {
            if (file != null && file.Length > 0)
                order.ImageUrl = await UploadFileToBlobStorageAndReturnUrl(file.OpenReadStream(), file.FileName);

            if (ModelState.IsValid)
            {
                await _tableStorageService.UpdateOrderAsync(order);
                TempData["message"] = "Order updated successfully!";
                return RedirectToAction("Index");
            }

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey, string? imageUrl)
        {
            if (!string.IsNullOrEmpty(imageUrl))
                await DeleteBlobAsync(imageUrl);

            await _tableStorageService.DeleteOrderAsync(partitionKey, rowKey);
            TempData["message"] = "Order deleted successfully!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePaymentOption(string partitionKey, string rowKey, string paymentOption)
        {
            var order = await _tableStorageService.GetOrderAsync(partitionKey, rowKey);
            if (order == null) return NotFound();

            order.PaymentOption = paymentOption;
            await _tableStorageService.UpdateOrderAsync(order);

            TempData["message"] = "Payment option updated successfully!";
            return RedirectToAction("Details", new { partitionKey = order.PartitionKey, rowKey = order.RowKey });
        }


        // ========================= BLOB METHODS =========================
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