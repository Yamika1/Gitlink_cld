using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.Models
{
    public class AddCustomerWithImage
    {
        [Required]
        public string? CustomerName { get; set; }

        [Required]
        public string? Surname { get; set; }

        [Required]
        public string? Email { get; set; }

        [Required]
        [Display(Name = "Customer Image")]

        public IFormFile? CustomerImage { get; set; }
    }
}
