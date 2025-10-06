using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueTrigger
{
    internal class ProductEntity : ITableEntity
    {
        public string? ProductName { get; set; }

        public string? ProductImage { get; set; }
        public string? ProductDescription { get; set; }

        public string PartitionKey { get; set; } = "Product";

        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}