using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.Models
{
    public class AddOrderWithImage
    {
        [Required]
        public string? OrderName { get; set; }

        [Required]
        public string? OrderType { get; set; }
        [Required]
        public string? OrderDescription { get; set; }

        [Required]
        [Display(Name = "Order Image")]

        public IFormFile? OrderImage { get; set; }
    }
}
