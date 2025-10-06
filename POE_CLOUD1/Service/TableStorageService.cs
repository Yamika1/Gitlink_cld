using Azure;
using Azure.Data.Tables;
using POE_CLOUD1.Models;

namespace POE_CLOUD1.Service
{
    public class TableStorageService
    {
        public readonly TableClient _tableClient;



        public TableStorageService(string connectionString)
        {

            _tableClient = new TableClient(connectionString, "OrderTable");


        }
        public async Task<List<Order>> GetAllOrdersAsync(string partitionKey)
        {
            var orders = new List<Order>();

            await foreach (var order in _tableClient.QueryAsync<Order>(o => o.PartitionKey == partitionKey))
            {
                orders.Add(order);
            }

            return orders;
        }
        public async Task AddOrdersAsync(Order order)
        {
            if (string.IsNullOrEmpty(order.PartitionKey) || string.IsNullOrEmpty(order.RowKey))
            {
                throw new ArgumentException("PartitionKey and rowkey must be set");
            }
            try
            {
                await _tableClient.AddEntityAsync(order);

            }
            catch (RequestFailedException ex)
            {
                throw new InvalidOperationException("Error adding entity to Table Storage", ex);
            }
        }
        public async Task<Order?> GetOrderAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<Order>(partitionKey, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
        public async Task<Order?> GetOrderByIdAsync(string rowKey)
        {
            await foreach (var order in _tableClient.QueryAsync<Order>(o => o.RowKey == rowKey))
            {
                return order;
            }
            return null;
        }



        public async Task UpdateOrderAsync(Order order)
        {
            if (string.IsNullOrEmpty(order.PartitionKey) || string.IsNullOrEmpty(order.RowKey))
            {
                throw new ArgumentException("PartitionKey and RowKey must be set");
            }

            await _tableClient.UpdateEntityAsync(order, ETag.All, TableUpdateMode.Replace);
        }
        public async Task DeleteOrderAsync(string partitionKey, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }
        public async Task<List<Product>> GetAllProductsAsync()
        {
            var products = new List<Product>();

            await foreach (var product in _tableClient.QueryAsync<Product>())
            {
                products.Add(product);

            }
            return products;
        }
        public async Task AddProductAsync(Product product)
        {
            if (string.IsNullOrEmpty(product.PartitionKey) || string.IsNullOrEmpty(product.RowKey))
            {
                throw new ArgumentException("PartitionKey and rowkey must be set");
            }
            try
            {
                await _tableClient.AddEntityAsync(product);

            }
            catch (RequestFailedException ex)
            {
                throw new InvalidOperationException("Error adding entity to Table Storage", ex);
            }
        }
        public async Task<Product?> GetProductByIdAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<Product>(partitionKey, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteProductAsync(string partitionKey, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }


        public async Task<List<Product>> GetAllItemsAsync()
        {
            var products = new List<Product>();
            await foreach (var product in _tableClient.QueryAsync<Product>())
            {
                products.Add(product);
            }
            return products;
        }

        public async Task UpdateProductAsync(Product product)
        {
            if (string.IsNullOrEmpty(product.PartitionKey) || string.IsNullOrEmpty(product.RowKey))
            {
                throw new ArgumentException("PartitionKey and RowKey must be set");
            }

            await _tableClient.UpdateEntityAsync(product, ETag.All, TableUpdateMode.Replace);
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            var customers = new List<Customer>();

            await foreach (var customer in _tableClient.QueryAsync<Customer>())
            {
                customers.Add(customer);
            }

            return customers;
        }
        public async Task AddCustomerAsync(Customer customer)
        {
            if (string.IsNullOrEmpty(customer.PartitionKey) || string.IsNullOrEmpty(customer.RowKey))
            {
                throw new ArgumentException("PartitionKey and rowkey must be set");
            }
            try
            {
                await _tableClient.AddEntityAsync(customer);

            }
            catch (RequestFailedException ex)
            {
                throw new InvalidOperationException("Error adding entity to Table Storage", ex);
            }
        }
        public async Task<Customer?> GetCustomerAsync(string partitionKey, string rowKey)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<Customer>(partitionKey, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
        public async Task<Customer?> GetCustomerByIdAsync(string partitionKey, string rowKey)
        {
            return await GetCustomerAsync(partitionKey, rowKey);
        }
        public async Task UpdateCustomerAsync(Customer customer)
        {
            if (string.IsNullOrEmpty(customer.PartitionKey) || string.IsNullOrEmpty(customer.RowKey))
            {
                throw new ArgumentException("PartitionKey and RowKey must be set");
            }

            await _tableClient.UpdateEntityAsync(customer, ETag.All, TableUpdateMode.Replace);
        }



        public async Task DeleteCustomerAsync(string partitionKey, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }
    }
}