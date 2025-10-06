using Azure.Storage.Queues;
using System.Text.Json;

namespace Queue
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var connectionString = "DefaultEndpointsProtocol=https;AccountName=st10438801yamika;AccountKey=2QW7REkGRY05gbaHY2ZpItXZFa876nEI3CGmdIYblVOcILVYw8bbcQ3w8E/n9WTtuX3+Bjz2xyPE+AStknCIzg==;EndpointSuffix=core.windows.net";

            var queueClient = new QueueClient(
               connectionString, "playlist", new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

            await queueClient.CreateIfNotExistsAsync();
            var order = new { OrderName = "Orders", OrderType = "order", OrderDescription = "its an order." };
            var product = new { ProductName = "Productss", ProductDescription = "it is a product" };
            var customer = new { CustomerName = "John M", Surname = "Smith" };

            string orderJson = JsonSerializer.Serialize(order);
            string productJson = JsonSerializer.Serialize(product);
            string customerJson = JsonSerializer.Serialize(customer);


            await queueClient.SendMessageAsync(orderJson);
            await queueClient.SendMessageAsync(productJson);
            await queueClient.SendMessageAsync(customerJson);

            Console.WriteLine($"Message sent (Order): {orderJson}");
            Console.WriteLine($"Message sent (Product): {productJson}");
            Console.WriteLine($"Message sent (Customer): {customerJson}");
        }
    }
}