using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>Small shared plumbing for the account-page tests.</summary>
internal static class HostedAccountTestSupport
{
    public const string Password = "Correct-h0rse-battery";

    public static async Task<HttpClient> SeedAndSignInAsync(
        WebApplicationFactory<WebHostDemoMode> factory, string email, string password = Password)
    {
        await WebHostIntegration.SeedUserAsync(factory, email, password);

        var client = WebHostIntegration.NewClient(factory);
        var login = await WebHostIntegration.LoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.True(WebHostIntegration.SetsAuthCookie(login), "sign-in must issue the session cookie");
        return client;
    }

    public static async Task<HttpResponseMessage> PostManageAsync(
        HttpClient client, string handler, params (string Key, string Value)[] fields)
    {
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/manage");
        return await client.PostAsync($"/terminal/account/manage?handler={handler}",
            WebHostIntegration.Form(new[] { ("__RequestVerificationToken", token) }
                .Concat(fields).ToArray()));
    }

    public static async Task<AppUser> UserAsync(
        WebApplicationFactory<WebHostDemoMode> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user!;
    }

    public static string UserFolder(string dataRoot, string userId) =>
        Path.Combine(dataRoot, "users", userId);
}
