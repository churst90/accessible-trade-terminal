using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;

        public RegisterModel(UserManager<AppUser> users, SignInManager<AppUser> signIn)
        {
            _users = users;
            _signIn = signIn;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public List<string> Errors { get; } = new();

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";

            [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password), Display(Name = "Password")]
            public string Password { get; set; } = "";

            [DataType(DataType.Password), Display(Name = "Confirm password")]
            [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
            public string ConfirmPassword { get; set; } = "";
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            var user = new AppUser
            {
                UserName = Input.Email,   // email is the username (RequireUniqueEmail)
                Email = Input.Email,
                CreatedUtc = DateTime.UtcNow,
                Tier = "free",
            };

            var result = await _users.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                await _signIn.SignInAsync(user, isPersistent: false);
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl);
            }

            Errors.AddRange(result.Errors.Select(e => e.Description));
            return Page();
        }
    }
}
