using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.Models
{
    public class Order : ITableEntity
    {
        [Key]

        public int OrderId { get; set; }

        public string? OrderName { get; set; }

        public string? OrderStatus { get; set; }

        public string? OrderType { get; set; }  

        public DateTime? DeliveryDate { get; set; }

        public string? ImageUrl { get; set; }
        public string? NewProduct { get; set; }

        public DateTime? OrderDate { get; set; }

        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;


        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string? PaymentOption { get; set; }
        public List<string> Products { get; set; } = new List<string>();
    }
}

