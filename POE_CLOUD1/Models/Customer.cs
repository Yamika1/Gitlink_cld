using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.Models
{
    public class Customer : ITableEntity
    {
        [Key]

        public int CustomerId { get; set; }

        public string? CustomerFirstName { get; set; }

        public string? CustomerSurname { get; set; }

        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }

        public string? ImageUrl { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? Country { get; set; }

        public string? PartitionKey { get; set; }

        public string? RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}
