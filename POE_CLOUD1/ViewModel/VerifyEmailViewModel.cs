using System.ComponentModel.DataAnnotations;

namespace POE_CLOUD1.ViewModel
{
    public class VerifyEmailViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        public string Email { get; set; }

    }
}