using Microsoft.AspNetCore.Mvc;
using Nevoweb.ETransactions.Models;
using Nevoweb.ETransactions.Services;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nevoweb.ETransactions.Controllers;

public class ETransactionsController : BasePublicController
{
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly ETransactionsRequestBuilder _eTransactionsRequestBuilder;
    private readonly ETransactionsPaymentSettings _settings;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;

    public ETransactionsController(ILocalizationService localizationService,
        INotificationService notificationService,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        ETransactionsRequestBuilder eTransactionsRequestBuilder,
        ETransactionsPaymentSettings settings,
        ISettingService settingService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _eTransactionsRequestBuilder = eTransactionsRequestBuilder;
        _settings = settings;
        _settingService = settingService;
        _storeContext = storeContext;
        _workContext = workContext;
    }

    [HttpGet]
    public async Task<IActionResult> RedirectToETransactions(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order is null || order.PaymentMethodSystemName != ETransactionsPaymentDefaults.SystemName)
            return RedirectToRoute(ETransactionsPaymentDefaults.Route.HomePage);

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order.CustomerId != customer.Id)
            return Challenge();

        var store = await _storeContext.GetCurrentStoreAsync();
        var settings = await _settingService.LoadSettingAsync<ETransactionsPaymentSettings>(store.Id);

        var effectueUrl = Url.RouteUrl(ETransactionsPaymentDefaults.Route.Return, new { orderId = order.Id, status = 1 }, Request.Scheme);
        var refuseUrl = Url.RouteUrl(ETransactionsPaymentDefaults.Route.Return, new { orderId = order.Id, status = 0 }, Request.Scheme);
        var annuleUrl = refuseUrl;
        var notifyUrl = Url.RouteUrl(ETransactionsPaymentDefaults.Route.Notify, null, Request.Scheme);

        var (postUrl, values) = await _eTransactionsRequestBuilder.BuildPostAsync(order, effectueUrl, refuseUrl, annuleUrl, notifyUrl);
        var model = new ETransactionsRedirectModel
        {
            PostUrl = postUrl,
            Inputs = values
        };

        if (settings.DebugMode)
        {
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                CreatedOnUtc = DateTime.UtcNow,
                Note = $"ETransactions redirect generated. URL: {postUrl}; Payload keys: {string.Join(",", values.Keys)}",
                DisplayToCustomer = false
            });
        }

        return View("~/Plugins/Payments.ETransactions/Views/Redirect.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Return(int orderId, int status)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order is null)
            return RedirectToRoute(ETransactionsPaymentDefaults.Route.HomePage);

        if (status == 0)
        {
            if (_orderProcessingService.CanCancelOrder(order))
                await _orderProcessingService.CancelOrderAsync(order, false);

            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Payment.ETransactions.PaymentFailed"));
            return RedirectToRoute(ETransactionsPaymentDefaults.Route.ShoppingCart);
        }

        if (order.PaymentStatus != PaymentStatus.Paid)
            _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Plugins.Payment.ETransactions.PaymentPending"));

        return RedirectToRoute(ETransactionsPaymentDefaults.Route.CheckoutCompleted, new { orderId });
    }

    [IgnoreAntiforgeryToken]
    [HttpGet, HttpPost]
    public async Task<IActionResult> Notify()
    {
        var parameters = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var pair in form)
                parameters[pair.Key] = pair.Value.ToString();
        }

        if (!parameters.TryGetValue("ref", out var refValue) || !int.TryParse(refValue, out var orderId))
            return Content("KO", "text/plain");

        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order is null)
            return Content("KO", "text/plain");

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (_settings.ValidateSourceIp && !IsAllowedIp(remoteIp, _settings.AllowedIps))
        {
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                CreatedOnUtc = DateTime.UtcNow,
                Note = $"ETransactions callback rejected because source IP is not allowed. Source: {remoteIp}",
                DisplayToCustomer = false
            });
            return Content("KO", "text/plain");
        }

        parameters.TryGetValue("rtnerr", out var returnCode);
        parameters.TryGetValue("auto", out var authorizationCode);
        parameters.TryGetValue("trans", out var transactionId);
        parameters.TryGetValue("call", out var callNumber);

        order.AuthorizationTransactionId = transactionId;
        order.AuthorizationTransactionCode = authorizationCode;
        order.AuthorizationTransactionResult = $"rtnerr={returnCode};call={callNumber}";
        await _orderService.UpdateOrderAsync(order);

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            CreatedOnUtc = DateTime.UtcNow,
            Note = $"ETransactions callback received ({remoteIp}). return={returnCode}, auto={authorizationCode}, trans={transactionId}, call={callNumber}",
            DisplayToCustomer = false
        });

        if (string.IsNullOrWhiteSpace(authorizationCode))
            return Content("KO", "text/plain");

        if (string.Equals(returnCode, "00000", StringComparison.OrdinalIgnoreCase))
        {
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
                await _orderProcessingService.MarkOrderAsPaidAsync(order);

            return Content("OK", "text/plain");
        }

        if (string.Equals(returnCode, "99999", StringComparison.OrdinalIgnoreCase))
        {
            if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                await _orderProcessingService.MarkAsAuthorizedAsync(order);

            return Content("OK", "text/plain");
        }

        if (_orderProcessingService.CanCancelOrder(order))
            await _orderProcessingService.CancelOrderAsync(order, false);

        return Content("KO", "text/plain");
    }

    protected virtual bool IsAllowedIp(string remoteIp, string allowedIps)
    {
        if (string.IsNullOrWhiteSpace(remoteIp))
            return false;

        var normalizedAllowedIps = (allowedIps ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!normalizedAllowedIps.Any())
            return false;

        return normalizedAllowedIps.Any(ip => string.Equals(ip, remoteIp, StringComparison.OrdinalIgnoreCase));
    }
}
