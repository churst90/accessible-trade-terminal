using System.Threading.Tasks;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<AppUser> _signIn;
        public LogoutModel(SignInManager<AppUser> signIn) => _signIn = signIn;

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            await _signIn.SignOutAsync();
            return Redirect("/account/login");
        }
    }
}
