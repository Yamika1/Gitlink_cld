using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using POE_CLOUD1.Service;

namespace POE_CLOUD1
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllersWithViews();
            builder.Services.AddHttpClient();

            // Get Azure Storage connection string from appsettings.json
            string azureStorageConnection = builder.Configuration.GetConnectionString("AzureStorage");

            // Register Azure services
            builder.Services.AddSingleton<TableStorageService>(sp => new TableStorageService(azureStorageConnection));
            builder.Services.AddSingleton<BlobService>(sp =>
      new BlobService(
          builder.Configuration.GetConnectionString("AzureStorage"),
          "blobcontainer" 
      ));
            builder.Services.AddSingleton<QueueService>(sp =>
            {
                var queueClient = new QueueClient(azureStorageConnection, "playlist");
                queueClient.CreateIfNotExists();
                return new QueueService(queueClient);
            });

            builder.Services.AddSingleton<AzureFileShareService>(sp =>
                new AzureFileShareService(azureStorageConnection, "yamikfileshare")
            );

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
