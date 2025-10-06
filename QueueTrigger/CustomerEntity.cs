using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueTrigger
{
    internal class CustomerEntity : ITableEntity
    {
        public string? CustomerName { get; set; }

        public string? CustomerImage { get; set; }
        public string? Surname { get; set; }

        public string PartitionKey { get; set; } = "Customer";

        public string RowKey { get; set; } = string.Empty;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
    }
}