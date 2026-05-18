using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nevoweb.ETransactions.Services;
using Nop.Core.Infrastructure;

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
    }

    public int Order => 1;
}
