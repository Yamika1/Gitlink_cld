using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using POE_CLOUD1.Models;
using POE_CLOUD1.Service;
using System.Text.Json;

namespace POE_CLOUD1.Controllers
{

    public class CustomerController : Controller
    {
        private readonly TableStorageService _tableStorageService;
        private readonly QueueService _svc;
        private readonly AzureFileShareService _fileShareService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public CustomerController(
            TableStorageService tableStorageService,
            AzureFileShareService fileShareService,
            QueueService queueService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _tableStorageService = tableStorageService;
            _fileShareService = fileShareService;
            _svc = queueService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // ========================= INDEX =========================
        public async Task<IActionResult> Index()
        {
            IEnumerable<Customer> customers = new List<Customer>();
            var httpClient = _httpClientFactory.CreateClient();
            var apiBaseUrl = _configuration["FunctionApi:BaseUrl"];

            try
            {
                var response = await httpClient.GetAsync($"{apiBaseUrl}customer");
                if (response.IsSuccessStatusCode)
                {
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    customers = await JsonSerializer.DeserializeAsync<IEnumerable<Customer>>(contentStream, options);
                }
                else
                {
                    ViewBag.ErrorMessage = "API returned an error while retrieving customers.";
                }
            }
            catch
            {
                ViewBag.ErrorMessage = "Could not connect to the API. Falling back to Table Storage.";
                customers = await _tableStorageService.GetAllCustomersAsync();
            }

            try { ViewBag.LocalFiles = await _fileShareService.ListFilesAsync("uploads"); }
            catch { ViewBag.LocalFiles = new List<FileModel>(); }

            try { ViewBag.QueueMessages = await _svc.PeekMessagesAsync(5); }
            catch { ViewBag.QueueMessages = new List<string>(); }

            return View(customers);
        }

        // ========================= CUSTOMER CRUD =========================
        [HttpGet]
        public IActionResult AddCustomer() => View();

        [HttpPost]
        public async Task<IActionResult> AddCustomer(Customer customer, IFormFile? file)
        {
            if (!ModelState.IsValid)
            {
                TempData["message"] = "Invalid details";
                return RedirectToAction("Index");
            }

            customer.PartitionKey = "Customer";
            customer.RowKey = Guid.NewGuid().ToString();

          
            await _tableStorageService.AddCustomerAsync(customer);
            TempData["message"] = "Customer added successfully!";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string partitionKey, string rowKey)
        {
            var customer = await _tableStorageService.GetCustomerByIdAsync(partitionKey, rowKey);
            if (customer == null) return NotFound();
            return View(customer);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Customer customer, IFormFile? file)
        {
            if (!ModelState.IsValid) return View(customer);

            await _tableStorageService.UpdateCustomerAsync(customer);
            TempData["message"] = "Customer updated successfully!";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Delete(string partitionKey, string rowKey)
        {
            var customer = await _tableStorageService.GetCustomerByIdAsync(partitionKey, rowKey);
            if (customer == null) return NotFound();
            return View(customer);
        }

        [HttpPost]
        public async Task<IActionResult> CustomerDelete(string partitionKey, string rowKey)
        {
            await _tableStorageService.DeleteCustomerAsync(partitionKey, rowKey);
            TempData["message"] = "Customer deleted successfully!";
            return RedirectToAction("Index");
        }
        [HttpGet]
        public async Task<IActionResult> Details(string partitionKey, string rowKey)
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
                return BadRequest();

            var customer = await _tableStorageService.GetCustomerByIdAsync(partitionKey, rowKey);
            if (customer == null) return NotFound();

            return View(customer); 
        }

        // ========================= FILE SHARE =========================
        [HttpGet]
        public IActionResult UploadFile() => View();

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