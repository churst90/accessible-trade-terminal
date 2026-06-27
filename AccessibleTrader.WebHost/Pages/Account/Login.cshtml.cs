using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signIn;
        public LoginModel(SignInManager<AppUser> signIn) => _signIn = signIn;

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? Error { get; set; }

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";

            [Required, DataType(DataType.Password), Display(Name = "Password")]
            public string Password { get; set; } = "";

            [Display(Name = "Keep me signed in")]
            public bool RememberMe { get; set; }
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            // Users register with email as their username, so sign in by email.
            var result = await _signIn.PasswordSignInAsync(
                Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl);

            Error = result.IsLockedOut
                ? "This account is temporarily locked after too many failed attempts. Please try again later."
                : "Email or password is incorrect.";
            return Page();
        }
    }
}
