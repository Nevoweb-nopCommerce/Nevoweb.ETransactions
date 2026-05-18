using Microsoft.AspNetCore.Mvc;
using Nevoweb.ETransactions.Models;
using Nop.Web.Framework.Components;

namespace Nevoweb.ETransactions.Components;

public class ETransactionsPaymentInfoViewComponent : NopViewComponent
{
    private readonly ETransactionsPaymentSettings _settings;

    public ETransactionsPaymentInfoViewComponent(ETransactionsPaymentSettings settings)
    {
        _settings = settings;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var model = new PaymentInfoModel
        {
            DescriptionText = _settings.DescriptionText
        };

        return Task.FromResult<IViewComponentResult>(View("~/Plugins/Payments.ETransactions/Views/PaymentInfo.cshtml", model));
    }
}
