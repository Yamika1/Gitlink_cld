using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using POE_CLOUD1.Models;

namespace POE_CLOUD1.Data
{
    public class AppDBContext : IdentityDbContext<Users>
    {
        public AppDBContext(DbContextOptions options) : base(options)
        {
        }
    }
}
