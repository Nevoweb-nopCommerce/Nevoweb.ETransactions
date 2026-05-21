using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nevoweb.ETransactions.Services;
using Nop.Core.Infrastructure;
using Nop.Services.Localization;

namespace Nevoweb.ETransactions.Infrastructure;

public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddScoped<ETransactionsRequestBuilder>();
    }

    public void Configure(IApplicationBuilder application)
    {
        // Seed locale resources that may have been added after initial plugin install.
        // This ensures admin labels appear correctly without requiring a plugin reinstall.
        using var scope = application.ApplicationServices.CreateScope();
        var localizationService = scope.ServiceProvider.GetService<ILocalizationService>();
        if (localizationService != null)
        {
            localizationService.AddOrUpdateLocaleResourceAsync(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Plugins.Payment.ETransactions.ValidateRsaSignature"] = "RSA Validation for IPN",
                ["Plugins.Payment.ETransactions.ValidateRsaSignature.Hint"] = "When enabled, the RSA-SHA1 signature from the gateway IPN 'sign' parameter is verified. Disable only for troubleshooting — never in production."
            }).GetAwaiter().GetResult();
        }
    }

    public int Order => 1;
}
