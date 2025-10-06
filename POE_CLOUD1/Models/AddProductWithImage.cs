using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.Models
{
    public class AddProductWithImage
    {
        [Required]
        public string? ProductName { get; set; }

        [Required]
        public string? ProductDescription { get; set; }

        [Required]
        [Display(Name = "Product Image")]

        public IFormFile? ProductImage { get; set; }
    }
}