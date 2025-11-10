using Azure.Storage.Queues;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

using Microsoft.EntityFrameworkCore;
using POE_CLOUD1.Data;
using POE_CLOUD1.Models;
using POE_CLOUD1.Service;

namespace POE_CLOUD1
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllersWithViews();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(opt =>
            {
                opt.Cookie.Name = ".MvcCartLite.Session";
                opt.IdleTimeout = TimeSpan.FromHours(2);
                opt.Cookie.HttpOnly = true;
            });

            builder.Services.AddSingleton<InMemoryCatalog>();

            builder.Services.AddDbContext<AppDBContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

            builder.Services.AddIdentity<Users, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequiredLength = 6;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
            })
                .AddEntityFrameworkStores<AppDBContext>()
                .AddDefaultTokenProviders();

            builder.Services.AddDistributedMemoryCache();

            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });


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
            await SeedService.SeedDatabase(app.Services);

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

            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
    public class InMemoryCatalog
    {
        public List<CatalogProduct> Products { get; } =
        [
            new CatalogProduct { Id = 1, Name = "Notebook", Price = 29.99m },
        new CatalogProduct { Id = 2, Name = "Pen", Price = 9.50m },
        new CatalogProduct { Id = 3, Name = "Stapler", Price = 79.00m },
    ];

        public CatalogProduct? Find(int id) => Products.FirstOrDefault(p => p.Id == id);
    }

    public record CatalogProduct
    {
        public int Id { get; init; }
        public string Name { get; init; }
        public decimal Price { get; set; }
    }

    public record CartItem
    {
        public int ProductId { get; init; }
        public string Name { get; init; }

        public decimal UnitPrice { get; init; }

        public int Quantity { get; init; }

        public decimal LineTotal => UnitPrice * Quantity;

    }
    public static class SessionExtensions
    {
        private static readonly System.Text.Json.JsonSerializerOptions _opts =
            new(System.Text.Json.JsonSerializerOptions.Default) { PropertyNameCaseInsensitive = true };

        public static void SetJson<T>(this ISession session, string key, T value) =>
            session.SetString(key, System.Text.Json.JsonSerializer.Serialize(value, _opts));

        public static T? GetJson<T>(this ISession session, string key)
        {
            var s = session.GetString(key);
            return s is null ? default : System.Text.Json.JsonSerializer.Deserialize<T>(s, _opts);
        }

    }
}



