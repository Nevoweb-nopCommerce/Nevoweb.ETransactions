using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nevoweb.ETransactions.Infrastructure;

public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(name: ETransactionsPaymentDefaults.Route.Configure,
            pattern: "Admin/ETransactions/Configure",
            defaults: new { controller = "PaymentETransactions", action = "Configure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: ETransactionsPaymentDefaults.Route.Redirect,
            pattern: "Plugins/ETransactions/Redirect/{orderId:int}",
            defaults: new { controller = "ETransactions", action = "RedirectToETransactions" });

        endpointRouteBuilder.MapControllerRoute(name: ETransactionsPaymentDefaults.Route.Return,
            pattern: "Plugins/ETransactions/Return",
            defaults: new { controller = "ETransactions", action = "Return" });

        endpointRouteBuilder.MapControllerRoute(name: ETransactionsPaymentDefaults.Route.Notify,
            pattern: "Plugins/ETransactions/Notify",
            defaults: new { controller = "ETransactions", action = "Notify" });
    }

    public int Priority => 0;
}
