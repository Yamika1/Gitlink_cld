using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using System.Text;

namespace POE_CLOUD1.Service
{
    public class QueueService
    {
        private readonly QueueClient _queueClient;

        public QueueService(QueueClient queue)
        {
            _queueClient = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public async Task SendAsync(string text)
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            await _queueClient.SendMessageAsync(base64);
        }
        public async Task<List<string>> PeekMessagesAsync(int maxMessages = 5)
        {
            var messages = new List<string>();
            var peeked = await _queueClient.PeekMessagesAsync(maxMessages);

            foreach (var msg in peeked.Value)
            {
         
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(msg.MessageText));
                messages.Add(decoded);
            }

            return messages;
        }
        public async Task<List<string>> GetMessagesAsync(int maxMessages = 5)
        {
            var messages = new List<string>();
            QueueMessage[] retrieved = await _queueClient.ReceiveMessagesAsync(maxMessages);

            foreach (var msg in retrieved)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(msg.MessageText));
                messages.Add(decoded);

              
                await _queueClient.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);
            }

            return messages;
        }
    }

}

