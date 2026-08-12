using System.ComponentModel.DataAnnotations;

namespace CloudStorage.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email wajib diisi")]
    [EmailAddress(ErrorMessage = "Format email tidak valid")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ingat Saya")]
    public bool RememberMe { get; set; }
}
