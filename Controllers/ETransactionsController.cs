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
using Nop.Core.Domain.Messages;
using Nop.Services.Catalog;
using Nop.Services.Logging;

namespace Nevoweb.ETransactions.Controllers;

public class ETransactionsController : BasePublicController
{
    private readonly ILogger _logger;
    private readonly IEmailAccountService _emailAccountService;
    private readonly IEmailSender _emailSender;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly ETransactionsRequestBuilder _eTransactionsRequestBuilder;
    private readonly ETransactionsPaymentSettings _settings;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;

    public ETransactionsController(ILogger logger,
        IEmailAccountService emailAccountService,
        IEmailSender emailSender,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        ETransactionsRequestBuilder eTransactionsRequestBuilder,
        ETransactionsPaymentSettings settings,
        ISettingService settingService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _logger = logger;
        _emailAccountService = emailAccountService;
        _emailSender = emailSender;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
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
            var payload = string.Join("\n", values.Select(kv =>
                kv.Key == "PBX_HMAC" ? $"  {kv.Key} = [hidden]" : $"  {kv.Key} = {kv.Value}"));
            var debugNote = $"[ETransactions DEBUG] Redirect to bank\nURL: {postUrl}\nPayload:\n{payload}";

            _logger.Information(debugNote);
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                CreatedOnUtc = DateTime.UtcNow,
                Note = debugNote,
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

        if (_settings.DebugMode)
        {
            var returnNote = $"[ETransactions DEBUG] Customer return received\nOrder: #{orderId}\nStatus: {status} ({(status == 1 ? "SUCCESS" : "CANCELLED/REFUSED")})\nCurrent payment status: {order.PaymentStatus}";
            _logger.Information(returnNote);
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                CreatedOnUtc = DateTime.UtcNow,
                Note = returnNote,
                DisplayToCustomer = false
            });
        }

        if (status == 0)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            // Restore cart items before cancelling the order
            var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
            foreach (var item in orderItems)
            {
                var product = await _productService.GetProductByIdAsync(item.ProductId);
                if (product is null)
                    continue;

                await _shoppingCartService.AddToCartAsync(
                    customer,
                    product,
                    ShoppingCartType.ShoppingCart,
                    store.Id,
                    item.AttributesXml,
                    item.UnitPriceExclTax,
                    item.RentalStartDateUtc,
                    item.RentalEndDateUtc,
                    item.Quantity);
            }

            if (_orderProcessingService.CanCancelOrder(order))
                await _orderProcessingService.CancelOrderAsync(order, false);

            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Payment.ETransactions.PaymentFailed"));
            return RedirectToRoute(ETransactionsPaymentDefaults.Route.ShoppingCart);
        }

        if (order.PaymentStatus != PaymentStatus.Paid)
        {
            // IPN has not been received yet — we cannot trust the browser return alone.
            // Do NOT mark as paid. Flag the order and alert the admin for manual verification.
            await RaiseIpnMissingAlertAsync(order);
        }

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

        if (_settings.DebugMode)
        {
            var allParams = string.Join("\n", parameters.Select(kv => $"  {kv.Key} = {kv.Value}"));
            var ipnNote = $"[ETransactions DEBUG] IPN/Notify received\nSource IP: {remoteIp}\nOrder: #{order.Id}\nParameters:\n{allParams}";
            _logger.Information(ipnNote);
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                CreatedOnUtc = DateTime.UtcNow,
                Note = ipnNote,
                DisplayToCustomer = false
            });
        }

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

            if (_settings.DebugMode)
                _logger.Information($"[ETransactions DEBUG] IPN outcome: Order #{order.Id} marked as PAID (rtnerr=00000, auto={authorizationCode}, trans={transactionId})");

            return Content("OK", "text/plain");
        }

        if (string.Equals(returnCode, "99999", StringComparison.OrdinalIgnoreCase))
        {
            if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                await _orderProcessingService.MarkAsAuthorizedAsync(order);

            if (_settings.DebugMode)
                _logger.Information($"[ETransactions DEBUG] IPN outcome: Order #{order.Id} marked as AUTHORIZED (rtnerr=99999, auto={authorizationCode}, trans={transactionId})");

            return Content("OK", "text/plain");
        }

        if (_orderProcessingService.CanCancelOrder(order))
            await _orderProcessingService.CancelOrderAsync(order, false);

        if (_settings.DebugMode)
            _logger.Warning($"[ETransactions DEBUG] IPN outcome: Order #{order.Id} CANCELLED/FAILED (rtnerr={returnCode}, auto={authorizationCode})");

        return Content("KO", "text/plain");
    }

    protected virtual async Task RaiseIpnMissingAlertAsync(Order order)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var adminUrl = $"{store.Url}Admin/Order/Edit/{order.Id}".Replace("//Admin", "/Admin");

        var alertMessage =
            $"⚠️ SECURITY ALERT — ETransactions IPN not received for Order #{order.Id}\n\n" +
            $"The customer browser returned a SUCCESS status from the payment gateway, " +
            $"but the bank's server-to-server IPN notification was NOT received.\n\n" +
            $"THIS ORDER MUST BE MANUALLY VERIFIED before processing or shipping.\n\n" +
            $"Possible causes:\n" +
            $"  • IPN delivery failed (firewall, timeout, server down at time of payment)\n" +
            $"  • Fraudulent browser return manipulation attempt\n\n" +
            $"Action required:\n" +
            $"  1. Log into your Paybox/ETransactions merchant back-office\n" +
            $"  2. Verify that a real transaction exists for order #{order.Id} (amount: {order.OrderTotal:C})\n" +
            $"  3. If confirmed paid → manually mark the order as Paid in the admin\n" +
            $"  4. If NOT found → cancel the order immediately\n\n" +
            $"Order admin link: {adminUrl}";

        // Write a highly visible order note
        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            CreatedOnUtc = DateTime.UtcNow,
            Note = alertMessage,
            DisplayToCustomer = false
        });

        // Log as Error so it appears red in Admin → System → Log
        _logger.Error($"[ETransactions] ⚠️ SECURITY ALERT — IPN missing for Order #{order.Id}. Payment status unverified. Manual check required.\n\n{alertMessage}");

        // Send alert email to the store owner using the default email account
        try
        {
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(
                (await _settingService.LoadSettingAsync<EmailAccountSettings>(store.Id)).DefaultEmailAccountId)
                ?? (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

            if (emailAccount is not null)
            {
                var subject = $"⚠️ ACTION REQUIRED — Unverified payment for Order #{order.Id} [{store.Name}]";
                var body = alertMessage.Replace("\n", "<br/>");

                await _emailSender.SendEmailAsync(
                    emailAccount,
                    subject,
                    body,
                    emailAccount.Email,
                    emailAccount.DisplayName,
                    emailAccount.Email,
                    store.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[ETransactions] Failed to send IPN-missing alert email for Order #{order.Id}: {ex.Message}", ex);
        }
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
