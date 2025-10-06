using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Files.Shares;
using Azure.Storage.Queues.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.Net;
using System.Text.Json;

namespace QueueTrigger;

public class Function1
{
    private readonly ILogger<Function1> _logger;
    private readonly TableClient _tableClient;
    private readonly BlobContainerClient _blobContainerClient;

    private readonly ShareClient _shareClient;
    private readonly ShareDirectoryClient _ordersDir;
    private readonly ShareDirectoryClient _productsDir;
    private readonly ShareDirectoryClient _customersDir;

    private readonly string _connectionString = "DefaultEndpointsProtocol=https;AccountName=st10438801yamika;AccountKey=2QW7REkGRY05gbaHY2ZpItXZFa876nEI3CGmdIYblVOcILVYw8bbcQ3w8E/n9WTtuX3+Bjz2xyPE+AStknCIzg==;EndpointSuffix=core.windows.net";
    private readonly string _containerName = "tester";

    public Function1(ILogger<Function1> logger)
    {
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Azure Storage connection string is missing.");
        }

        var tableServiceClient = new TableServiceClient(_connectionString);
        _tableClient = tableServiceClient.GetTableClient("OrderTable");
        _tableClient.CreateIfNotExists();

        _blobContainerClient = new BlobContainerClient(_connectionString, _containerName);
        _blobContainerClient.CreateIfNotExists(PublicAccessType.Blob);

        _shareClient = new ShareClient(_connectionString, "yamikfileshare");
        _shareClient.CreateIfNotExists();

        var rootDir = _shareClient.GetRootDirectoryClient();
        _ordersDir = rootDir.GetSubdirectoryClient("uploads/orders");
        _ordersDir.CreateIfNotExists();

        _productsDir = rootDir.GetSubdirectoryClient("uploads/products");
        _productsDir.CreateIfNotExists();

        _customersDir = rootDir.GetSubdirectoryClient("uploads/customers");
        _customersDir.CreateIfNotExists();
    }



    [Function(nameof(QueueOrderSender))]
    public async Task QueueOrderSender([QueueTrigger("playlist", Connection = "connection")] QueueMessage message)
    {
        _logger.LogInformation($"Queue Trigger processed Order: {message.MessageText}");

        await _tableClient.CreateIfNotExistsAsync();

        var order = JsonSerializer.Deserialize<OrderEnity>(message.MessageText);
        if (order == null)
        {
            _logger.LogError("Failed to deserialize Order JSON");
            return;
        }

        order.RowKey = Guid.NewGuid().ToString();
        order.PartitionKey = "Order";
        await _tableClient.AddEntityAsync(order);
        _logger.LogInformation($"Order saved: {order.RowKey}");
    }


    [Function("GetOrder")]
    public async Task<HttpResponseData> GetOrder([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "order")] HttpRequestData req)
    {
        var orders = await _tableClient.QueryAsync<OrderEnity>().ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(orders);
        return response;
    }

    [Function("GetOrdersWithImage")]
    public async Task<HttpResponseData> GetOrdersWithImage(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders-with-image")] HttpRequestData req)
    {
        try
        {
            var orders = await _tableClient.QueryAsync<OrderEnity>(x => x.PartitionKey == "Order").ToListAsync();
            var containerClient = new BlobContainerClient(_connectionString, "orderimages");
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            foreach (var order in orders)
            {
                if (!string.IsNullOrWhiteSpace(order.OrderImage))
                {
                    var blobClient = containerClient.GetBlobClient(Path.GetFileName(order.OrderImage));
                    if (await blobClient.ExistsAsync())
                    {
                        var sasUri = blobClient.GenerateSasUri(
                            Azure.Storage.Sas.BlobSasPermissions.Read,
                            DateTimeOffset.UtcNow.AddHours(1));
                        order.OrderImage = sasUri.ToString();
                    }
                    else
                    {
                        order.OrderImage = null;
                    }
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(orders);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching orders with images.");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync("Failed to fetch orders.");
            return response;
        }
    }

    [Function("AddOrderWithImage")]
    public async Task<HttpResponseData> AddOrderWithImage(
       [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "order-with-image")] HttpRequestData req)
    {
        _logger.LogInformation("Received request to add order with image.");

        var newOrder = new OrderEnity();
        string? uploadedBlobUrl = null;

        try
        {
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                _logger.LogWarning("Missing Content-Type header.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains("boundary="))
            {
                _logger.LogWarning("Invalid or missing multipart boundary in Content-Type.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var boundary = HeaderUtilities.RemoveQuotes(contentType.Split("boundary=")[1]).Value;
            var reader = new MultipartReader(boundary, req.Body);

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync().ConfigureAwait(false)) != null)
            {
                if (!section.Headers.ContainsKey("Content-Disposition"))
                    continue;

                var contentDisposition = section.Headers["Content-Disposition"].ToString();
                var parts = contentDisposition.Split(';').Select(p => p.Trim()).ToArray();

                var namePart = parts.FirstOrDefault(p => p.StartsWith("name=", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(namePart)) continue;

                var name = namePart.Split('=')[1].Trim('"');

                if (name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    newOrder.OrderName = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }
                else if (name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    newOrder.OrderDescription = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }

                else if (name.Equals("Type", StringComparison.OrdinalIgnoreCase))
                {
                    newOrder.OrderDescription = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }
                else if (name.Equals("OrderImage", StringComparison.OrdinalIgnoreCase))
                {
                    var fileNamePart = parts.FirstOrDefault(p => p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(fileNamePart)) continue;

                    var fileName = fileNamePart.Split('=')[1].Trim('"');
                    var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

                    var blobClient = _blobContainerClient.GetBlobClient(uniqueFileName);
                    await blobClient.UploadAsync(section.Body, overwrite: true).ConfigureAwait(false);

                    uploadedBlobUrl = blobClient.Uri.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(newOrder.OrderName) ||
                string.IsNullOrWhiteSpace(newOrder.OrderDescription) ||
                string.IsNullOrWhiteSpace(uploadedBlobUrl))
            {
                _logger.LogWarning("Order creation failed due to missing required fields.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            newOrder.PartitionKey = "Order";
            newOrder.RowKey = Guid.NewGuid().ToString();
            newOrder.OrderImage = uploadedBlobUrl;

            await _tableClient.AddEntityAsync(newOrder).ConfigureAwait(false);

            _logger.LogInformation("Successfully created order {OrderName} with image {ImageUrl}",
                newOrder.OrderName, uploadedBlobUrl);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteStringAsync("Order created successfully.").ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing AddOrderWithImage request.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("GetOrderFiles")]
    public async Task<HttpResponseData> GetOrderFiles(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "order/files")] HttpRequestData req)
    {
        try
        {
            var files = new List<string>();
            await foreach (var item in _ordersDir.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                    files.Add(item.Name);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(files);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOrderFiles failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("UploadOrderFile")]
    public async Task<HttpResponseData> UploadOrderFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "order/upload/{fileName}")] HttpRequestData req, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _ordersDir.GetFileClient(fileName);
            using var stream = new MemoryStream();
            await req.Body.CopyToAsync(stream);
            stream.Position = 0;
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Order file '{fileName}' uploaded successfully.");
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "UploadOrderFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("DownloadOrderFile")]
    public async Task<HttpResponseData> DownloadOrderFile(
           [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "order/download/{fileName}")] HttpRequestData req, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _ordersDir.GetFileClient(fileName);
            if (!await fileClient.ExistsAsync())
                return req.CreateResponse(HttpStatusCode.NotFound);

            var downloadInfo = await fileClient.DownloadAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");
            await downloadInfo.Value.Content.CopyToAsync(response.Body);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "DownloadOrderFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    [Function(nameof(QueueProductSender))]
    public async Task QueueProductSender([QueueTrigger("playlist", Connection = "connection")] QueueMessage message)
    {
        _logger.LogInformation($"Queue Trigger processed Product: {message.MessageText}");

        await _tableClient.CreateIfNotExistsAsync();

        var product = JsonSerializer.Deserialize<ProductEntity>(message.MessageText);
        if (product == null)
        {
            _logger.LogError("Failed to deserialize Product JSON");
            return;
        }

        product.RowKey = Guid.NewGuid().ToString();
        product.PartitionKey = "Product";
        await _tableClient.AddEntityAsync(product);
        _logger.LogInformation($"Product saved: {product.RowKey}");
    }

    [Function("GetProduct")]
    public async Task<HttpResponseData> GetProduct([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product")] HttpRequestData req)
    {
        var products = await _tableClient.QueryAsync<ProductEntity>().ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(products);
        return response;
    }

    [Function("GetProductsWithImage")]
    public async Task<HttpResponseData> GetProductsWithImage(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products-with-image")] HttpRequestData req)
    {
        try
        {
            var products = await _tableClient.QueryAsync<ProductEntity>(x => x.PartitionKey == "Product").ToListAsync();
            var containerClient = new BlobContainerClient(_connectionString, "productimages");
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            foreach (var product in products)
            {
                if (!string.IsNullOrWhiteSpace(product.ProductImage))
                {
                    var blobClient = containerClient.GetBlobClient(Path.GetFileName(product.ProductImage));
                    if (await blobClient.ExistsAsync())
                    {
                        var sasUri = blobClient.GenerateSasUri(
                            Azure.Storage.Sas.BlobSasPermissions.Read,
                            DateTimeOffset.UtcNow.AddHours(1));
                        product.ProductImage = sasUri.ToString();
                    }
                    else
                    {
                        product.ProductImage = null;
                    }
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(products);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching products with images.");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync("Failed to fetch products.");
            return response;
        }
    }

    [Function("AddProductWithImage")]
    public async Task<HttpResponseData> AddProductWithImage(
       [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "product-with-image")] HttpRequestData req)
    {
        _logger.LogInformation("Received request to add order with image.");

        var product = new ProductEntity();
        string? uploadedBlobUrl = null;

        try
        {
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                _logger.LogWarning("Missing Content-Type header.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains("boundary="))
            {
                _logger.LogWarning("Invalid or missing multipart boundary in Content-Type.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var boundary = HeaderUtilities.RemoveQuotes(contentType.Split("boundary=")[1]).Value;
            var reader = new MultipartReader(boundary, req.Body);

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync().ConfigureAwait(false)) != null)
            {
                if (!section.Headers.ContainsKey("Content-Disposition"))
                    continue;

                var contentDisposition = section.Headers["Content-Disposition"].ToString();
                var parts = contentDisposition.Split(';').Select(p => p.Trim()).ToArray();

                var namePart = parts.FirstOrDefault(p => p.StartsWith("name=", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(namePart)) continue;

                var name = namePart.Split('=')[1].Trim('"');

                if (name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    product.ProductName = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }
                else if (name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    product.ProductDescription = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }

                else if (name.Equals("ProductImage", StringComparison.OrdinalIgnoreCase))
                {
                    var fileNamePart = parts.FirstOrDefault(p => p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(fileNamePart)) continue;

                    var fileName = fileNamePart.Split('=')[1].Trim('"');
                    var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

                    var blobClient = _blobContainerClient.GetBlobClient(uniqueFileName);
                    await blobClient.UploadAsync(section.Body, overwrite: true).ConfigureAwait(false);

                    uploadedBlobUrl = blobClient.Uri.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(product.ProductName) ||
                string.IsNullOrWhiteSpace(product.ProductDescription) ||
                string.IsNullOrWhiteSpace(uploadedBlobUrl))
            {
                _logger.LogWarning("Product creation failed due to missing required fields.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            product.PartitionKey = "Product";
            product.RowKey = Guid.NewGuid().ToString();
            product.ProductImage = uploadedBlobUrl;

            await _tableClient.AddEntityAsync(product).ConfigureAwait(false);

            _logger.LogInformation("Successfully created Product {ProductName} with image {ProductImage}",
                 product.ProductName, uploadedBlobUrl);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteStringAsync("Product created successfully.").ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing AddProductWithImage request.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("GetProductFiles")]
    public async Task<HttpResponseData> GetProductFiles(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product/files")] HttpRequestData req)
    {
        try
        {
            var files = new List<string>();
            await foreach (var item in _productsDir.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                    files.Add(item.Name);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(files);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetProductFiles failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("UploadProductFile")]
    public async Task<HttpResponseData> UploadProductFile(
       [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "product/upload/{fileName}")] HttpRequestData req, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _productsDir.GetFileClient(fileName);
            using var stream = new MemoryStream();
            await req.Body.CopyToAsync(stream);
            stream.Position = 0;
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Product file '{fileName}' uploaded successfully.");
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "UploadProductFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("DownloadProductFile")]
    public async Task<HttpResponseData> DownloadProductFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product/download/{fileName}")] HttpRequestData req, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _productsDir.GetFileClient(fileName);
            if (!await fileClient.ExistsAsync())
                return req.CreateResponse(HttpStatusCode.NotFound);

            var downloadInfo = await fileClient.DownloadAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");
            await downloadInfo.Value.Content.CopyToAsync(response.Body);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "DownloadProductFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    [Function(nameof(QueueCustomerSender))]
    public async Task QueueCustomerSender([QueueTrigger("playlist", Connection = "connection")] QueueMessage message)
    {
        _logger.LogInformation($"Queue Trigger processed Customer: {message.MessageText}");

        await _tableClient.CreateIfNotExistsAsync();

        var customer = JsonSerializer.Deserialize<CustomerEntity>(message.MessageText);
        if (customer == null)
        {
            _logger.LogError("Failed to deserialize Customer JSON");
            return;
        }

        customer.RowKey = Guid.NewGuid().ToString();
        customer.PartitionKey = "Customer";
        await _tableClient.AddEntityAsync(customer);
        _logger.LogInformation($"Customer saved: {customer.RowKey}");
    }

    [Function("GetCustomer")]
    public async Task<HttpResponseData> GetCustomer([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customer")] HttpRequestData req)
    {
        var customers = await _tableClient.QueryAsync<CustomerEntity>().ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(customers);
        return response;
    }
    [Function("GetCustomersWithImage")]
    public async Task<HttpResponseData> GetCustomersWithImage(
       [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers-with-image")] HttpRequestData req)
    {
        try
        {
   
            var customers = await _tableClient.QueryAsync<CustomerEntity>(x => x.PartitionKey == "Customer").ToListAsync();

           
            var containerClient = new BlobContainerClient(_connectionString, "tester");
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            foreach (var customer in customers)
            {
                if (!string.IsNullOrWhiteSpace(customer.CustomerImage))
                {
                    var blobClient = containerClient.GetBlobClient(Path.GetFileName(customer.CustomerImage));

                    
                    if (await blobClient.ExistsAsync())
                    {  var sasUri = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
                        customer.CustomerImage = sasUri.ToString();
                    }
                    else
                    {
                      
                        customer.CustomerImage = null;
                    }
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(customers);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching customers with images.");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync("Failed to fetch customers.");
            return response;
        }
    }

    [Function("AddCustomerWithImage")]
    public async Task<HttpResponseData> AddCustomerWithImage(
     [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customer-with-image")] HttpRequestData req)
    {
        _logger.LogInformation("Received request to add customer with image.");

        var customer = new CustomerEntity();
        string? uploadedBlobUrl = null;

        try
        {
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                _logger.LogWarning("Missing Content-Type header.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var contentType = contentTypeValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains("boundary="))
            {
                _logger.LogWarning("Invalid or missing multipart boundary in Content-Type.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var boundary = HeaderUtilities.RemoveQuotes(contentType.Split("boundary=")[1]).Value;
            var reader = new MultipartReader(boundary, req.Body);

            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync().ConfigureAwait(false)) != null)
            {
                if (!section.Headers.ContainsKey("Content-Disposition"))
                    continue;

                var contentDisposition = section.Headers["Content-Disposition"].ToString();
                var parts = contentDisposition.Split(';').Select(p => p.Trim()).ToArray();

                var namePart = parts.FirstOrDefault(p => p.StartsWith("name=", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(namePart)) continue;

                var name = namePart.Split('=')[1].Trim('"');

                if (name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    customer.CustomerName = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }
                else if (name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                {
                    customer.Surname = await new StreamReader(section.Body).ReadToEndAsync().ConfigureAwait(false);
                }
                else if (name.Equals("CustomerImage", StringComparison.OrdinalIgnoreCase))
                {
                    var fileNamePart = parts.FirstOrDefault(p => p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(fileNamePart)) continue;

                    var fileName = fileNamePart.Split('=')[1].Trim('"');
                    var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

                   
                    var containerClient = new BlobContainerClient(_connectionString, "customerimages");
                    await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                    var blobClient = containerClient.GetBlobClient(uniqueFileName);
                    await blobClient.UploadAsync(section.Body, overwrite: true).ConfigureAwait(false);

                    uploadedBlobUrl = blobClient.Uri.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(customer.CustomerName) ||
                string.IsNullOrWhiteSpace(customer.Surname) ||
                string.IsNullOrWhiteSpace(uploadedBlobUrl))
            {
                _logger.LogWarning("Customer creation failed due to missing required fields.");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            customer.PartitionKey = "Customer";
            customer.RowKey = Guid.NewGuid().ToString();
            customer.CustomerImage = uploadedBlobUrl;

            await _tableClient.AddEntityAsync(customer).ConfigureAwait(false);

            _logger.LogInformation("Successfully created Customer {CustomerName} with image {CustomerImage}",
                customer.CustomerName, uploadedBlobUrl);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteStringAsync("Customer created successfully.").ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing AddCustomerWithImage request.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    [Function("GetCustomerFiles")]
    public async Task<HttpResponseData> GetCustomerFiles(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customer/files")] HttpRequestData req)
    {
        try
        {
            var files = new List<string>();
            await foreach (var item in _customersDir.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                    files.Add(item.Name);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(files);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCustomerFiles failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
    [Function("UploadCustomerFile")]
    public async Task<HttpResponseData> UploadCustomerFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customer/upload/{fileName}")] HttpRequestData req, string fileName)
    { 
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _customersDir.GetFileClient(fileName);
            using var stream = new MemoryStream();
            await req.Body.CopyToAsync(stream);
            stream.Position = 0;
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Customer file '{fileName}' uploaded successfully.");
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "UploadCustomerFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    [Function("DownloadCustomerFile")]
    public async Task<HttpResponseData> DownloadCustomerFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customer/download/{fileName}")] HttpRequestData req,string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        try
        {
            var fileClient = _customersDir.GetFileClient(fileName);
            if (!await fileClient.ExistsAsync())
                return req.CreateResponse(HttpStatusCode.NotFound);

            var downloadInfo = await fileClient.DownloadAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");
            await downloadInfo.Value.Content.CopyToAsync(response.Body);
            return response;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "DownloadCustomerFile failed.");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}