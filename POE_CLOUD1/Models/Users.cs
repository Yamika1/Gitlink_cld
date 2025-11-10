using Microsoft.AspNetCore.Identity;


namespace POE_CLOUD1.Models
{
    public class Users : IdentityUser
    {
        public string FullName { get; set; }
    }
}
