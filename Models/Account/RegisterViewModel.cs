using System.ComponentModel.DataAnnotations;

namespace CloudStorage.Models.Account;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Nama tampilan wajib diisi")]
    [Display(Name = "Nama Tampilan")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi")]
    [EmailAddress(ErrorMessage = "Format email tidak valid")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi")]
    [StringLength(100, ErrorMessage = "{0} minimal {2} karakter.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Konfirmasi Password")]
    [Compare("Password", ErrorMessage = "Password dan Konfirmasi Password tidak cocok.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
