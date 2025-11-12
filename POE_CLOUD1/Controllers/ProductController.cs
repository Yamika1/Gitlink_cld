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
        private readonly InMemoryCatalog _catalog;

        private readonly string _connectionString;
        private readonly string _containerName;

        public ProductController(
            TableStorageService tableStorageService,
            AzureFileShareService fileShareService,
            QueueService svc,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            InMemoryCatalog catalog)
        {
            _tableStorageService = tableStorageService;
            _fileShareService = fileShareService;
            _svc = svc;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _catalog = catalog;

            _connectionString = _configuration.GetConnectionString("AzureStorage");
            _containerName = _configuration["BlobStorage:Container"];
        }

        // ========================= INDEX =========================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Start with local in-memory catalog products
            var allProducts = _catalog.Products
                .Select(p => new Product
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    ProductPrice = p.Price
                })
                .ToList();

           
            try
            {
                var tableProducts = await _tableStorageService.GetAllProductsAsync("Product");
                if (tableProducts != null)
                    allProducts.AddRange(tableProducts);
            }
            catch
            {
                ViewBag.ErrorMessage = "Could not retrieve products from Table Storage.";
            }

     
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var apiBaseUrl = _configuration["FunctionApi:BaseUrl"];
                var response = await httpClient.GetAsync($"{apiBaseUrl}product");

                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var apiProducts = await JsonSerializer.DeserializeAsync<IEnumerable<Product>>(stream, options);
                    if (apiProducts != null) allProducts.AddRange(apiProducts);
                }
            }
            catch
            {
                ViewBag.ErrorMessage ??= "Could not connect to API.";
            }

            // File Share files
            try { ViewBag.LocalFiles = await _fileShareService.ListFilesAsync("uploads"); }
            catch { ViewBag.LocalFiles = new List<FileModel>(); }

            // Blob files
            try { ViewBag.BlobFiles = await FetchBlobUrlsAsync(); }
            catch { ViewBag.BlobFiles = new List<string>(); }

            // Queue messages
            try { ViewBag.QueueMessages = await _svc.PeekMessagesAsync(5); }
            catch { ViewBag.QueueMessages = new List<string>(); }

            return View(allProducts);
        }
        [HttpGet]
        public IActionResult AddProduct() => View(new Product());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProduct(Product product, IFormFile? file)
        {
            if (file != null && file.Length > 0)
                product.ImageURL = await UploadFileToBlobStorageAndReturnUrl(file.OpenReadStream(), file.FileName);

            if (ModelState.IsValid)
            {
                product.PartitionKey = "Product";
                product.RowKey = Guid.NewGuid().ToString();
                product.Timestamp = DateTimeOffset.UtcNow;

                await _tableStorageService.AddProductAsync(product);

                TempData["message"] = "product added successfully!";
                return RedirectToAction("Index");
            }

            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
                return NotFound();

            var product = await _tableStorageService.GetProductByIdAsync(partitionKey, rowKey);
            if (product == null) return NotFound();

            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
                return NotFound();

            var customer = await _tableStorageService.GetCustomerByIdAsync(partitionKey, rowKey);
            if (customer == null) return NotFound();

            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Product product, IFormFile? file)
        {
            if (file != null && file.Length > 0)
                product.ImageURL = await UploadFileToBlobStorageAndReturnUrl(file.OpenReadStream(), file.FileName);

            if (ModelState.IsValid)
            {
                await _tableStorageService.UpdateProductAsync(product);
                TempData["message"] = "product updated successfully!";
                return RedirectToAction("Index");
            }

            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey, string? imageUrl)
        {
            if (!string.IsNullOrEmpty(imageUrl))
                await DeleteBlobAsync(imageUrl);

            await _tableStorageService.DeleteCustomerAsync(partitionKey, rowKey);
            TempData["message"] = "product deleted successfully!";
            return RedirectToAction("Index");
        }

        // ========================= ADD TO CART =========================
        [HttpGet]
        public IActionResult AddToCart(int id)
        {
            var product = _catalog.Find(id);
            if (product == null) return NotFound();

            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int id, int qty = 1)
        {
            var product = _catalog.Find(id);

          
            if (product == null)
            {
                var products = await _tableStorageService.GetAllProductsAsync("Product");
                var apiProduct = products.FirstOrDefault(p => p.ProductId == id);

                if (apiProduct != null)
                {
                    product = new CatalogProduct
                    {
                        Id = apiProduct.ProductId,
                        Name = apiProduct.ProductName,
                        Price = (decimal)apiProduct.ProductPrice
                    };
                }
            }

            if (product == null)
                return NotFound($"Product with ID {id} not found.");

           
            var data = HttpContext.Session.GetString("CART");
            var cart = data == null ? new List<CartItem>() :
                       JsonSerializer.Deserialize<List<CartItem>>(data) ?? new List<CartItem>();

     
            var existing = cart.FirstOrDefault(c => c.ProductId == id);
            if (existing == null)
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    UnitPrice = product.Price,
                    Quantity = Math.Max(1, qty)
                });
            }
            else
            {
                var updated = existing with { Quantity = existing.Quantity + Math.Max(1, qty) };
                cart.Remove(existing);
                cart.Add(updated);
            }

            // Save to session
            var json = JsonSerializer.Serialize(cart.OrderBy(c => c.ProductId).ToList());
            HttpContext.Session.SetString("CART", json);

            TempData["msg"] = $"{product.Name} added to cart";

            return RedirectToAction("Index", "Cart");
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadFile(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["message"] = "Please select a file to upload";
                return RedirectToAction("Index");
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

        // ========================= QUEUE MESSAGE =========================
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