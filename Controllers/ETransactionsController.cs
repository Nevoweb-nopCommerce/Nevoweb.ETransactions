using Microsoft.AspNetCore.Mvc;
using Nevoweb.ETransactions.Models;
using Nevoweb.ETransactions.Services;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nevoweb.ETransactions.Controllers;

public class ETransactionsController : BasePublicController
{
    private readonly ILogger _logger;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly MessagesSettings _messagesSettings;
    private readonly IEmailAccountService _emailAccountService;
    private readonly ILanguageService _languageService;
    private readonly ILocalizedEntityService _localizedEntityService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IMessageTokenProvider _messageTokenProvider;
    private readonly INotificationService _notificationService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IWorkflowMessageService _workflowMessageService;
    private readonly ETransactionsRequestBuilder _eTransactionsRequestBuilder;
    private readonly ETransactionsPaymentSettings _settings;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;

    public ETransactionsController(ILogger logger,
        EmailAccountSettings emailAccountSettings,
        MessagesSettings messagesSettings,
        IEmailAccountService emailAccountService,
        ILanguageService languageService,
        ILocalizedEntityService localizedEntityService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IMessageTokenProvider messageTokenProvider,
        INotificationService notificationService,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IWorkflowMessageService workflowMessageService,
        ETransactionsRequestBuilder eTransactionsRequestBuilder,
        ETransactionsPaymentSettings settings,
        ISettingService settingService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _logger = logger;
        _emailAccountSettings = emailAccountSettings;
        _messagesSettings = messagesSettings;
        _emailAccountService = emailAccountService;
        _languageService = languageService;
        _localizedEntityService = localizedEntityService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _messageTokenProvider = messageTokenProvider;
        _notificationService = notificationService;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _workflowMessageService = workflowMessageService;
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

        // Write a highly visible order note
        var orderNote = $"⚠️ SECURITY ALERT — ETransactions IPN not received for Order #{order.Id}. " +
                        $"Customer browser returned SUCCESS but bank IPN was NOT received. " +
                        $"THIS ORDER MUST BE MANUALLY VERIFIED in the Paybox back-office before processing or shipping.";

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            CreatedOnUtc = DateTime.UtcNow,
            Note = orderNote,
            DisplayToCustomer = false
        });

        // Log as Error so it appears red in Admin → System → Log
        _logger.Error($"[ETransactions] ⚠️ SECURITY ALERT — IPN missing for Order #{order.Id}. Payment status unverified. Manual check required.");

        // Send via nopCommerce message template (editable in Admin → Content Management → Message templates)
        try
        {
            _logger.Information($"[ETransactions] Looking up template '{ETransactionsPaymentDefaults.IpnMissingMessageTemplateName}' for store {store.Id} ('{store.Name}')");

            // Resolve active templates for this store (falls back to global if none store-specific)
            var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(
                ETransactionsPaymentDefaults.IpnMissingMessageTemplateName, store.Id);

            if (templates == null || !templates.Any())
            {
                _logger.Information($"[ETransactions] No store-specific template found, falling back to global lookup.");
                templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(
                    ETransactionsPaymentDefaults.IpnMissingMessageTemplateName);
            }

            _logger.Information($"[ETransactions] Found {templates?.Count ?? 0} template(s) before active filter.");

            // Self-heal: if no template exists at all, create it now
            if (templates == null || !templates.Any())
            {
                _logger.Warning($"[ETransactions] Template '{ETransactionsPaymentDefaults.IpnMissingMessageTemplateName}' not found in database — creating it now.");
                templates = new List<MessageTemplate> { await EnsureAlertTemplateExistsAsync() };
            }

            // Only send active templates
            var inactiveCount = templates.Count(t => !t.IsActive);
            if (inactiveCount > 0)
                _logger.Warning($"[ETransactions] {inactiveCount} template(s) named '{ETransactionsPaymentDefaults.IpnMissingMessageTemplateName}' exist but are INACTIVE — enable them in Admin → Content Management → Message Templates.");

            templates = templates.Where(t => t.IsActive).ToList();

            if (!templates.Any())
            {
                _logger.Warning($"[ETransactions] All matching templates are inactive — no alert email queued for Order #{order.Id}.");
                return;
            }

            foreach (var template in templates)
            {
                _logger.Information($"[ETransactions] Processing template Id={template.Id}, EmailAccountId={template.EmailAccountId}, IsActive={template.IsActive}");

                // Resolve email account the same way nopCommerce core does
                var emailAccountId = template.EmailAccountId > 0
                    ? template.EmailAccountId
                    : _emailAccountSettings.DefaultEmailAccountId;

                _logger.Information($"[ETransactions] Resolved emailAccountId={emailAccountId} (DefaultEmailAccountId={_emailAccountSettings.DefaultEmailAccountId})");

                var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId)
                    ?? (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

                if (emailAccount is null)
                {
                    _logger.Warning($"[ETransactions] No email account found (id={emailAccountId}) for IPN-missing alert, Order #{order.Id}. Configure an email account in Admin → Configuration → Email Accounts.");
                    continue;
                }

                _logger.Information($"[ETransactions] Using email account '{emailAccount.Email}' (Id={emailAccount.Id})");

                // Resolve language — prefer the store's first published language
                var languageId = (await _languageService.GetAllLanguagesAsync(storeId: store.Id))
                    .FirstOrDefault(l => l.Published)?.Id ?? 0;
                if (languageId == 0)
                    languageId = (await _languageService.GetAllLanguagesAsync())
                        .FirstOrDefault(l => l.Published)?.Id ?? 0;

                _logger.Information($"[ETransactions] Using languageId={languageId}");

                // Build tokens — same set used by core order notifications
                var tokens = new List<Token>();
                await _messageTokenProvider.AddOrderTokensAsync(tokens, order, languageId);
                await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount, languageId);

                // Resolve store-owner recipient (honours MessagesSettings.UseDefaultEmailAccountForSendStoreOwnerEmails)
                EmailAccount ownerAccount;
                if (_messagesSettings.UseDefaultEmailAccountForSendStoreOwnerEmails)
                    ownerAccount = await _emailAccountService.GetEmailAccountByIdAsync(_emailAccountSettings.DefaultEmailAccountId) ?? emailAccount;
                else
                    ownerAccount = emailAccount;

                _logger.Information($"[ETransactions] Sending alert to '{ownerAccount.Email}' (UseDefaultForOwner={_messagesSettings.UseDefaultEmailAccountForSendStoreOwnerEmails})");

                var queuedId = await _workflowMessageService.SendNotificationAsync(
                    template,
                    emailAccount,
                    languageId,
                    tokens,
                    toEmailAddress: ownerAccount.Email,
                    toName: ownerAccount.DisplayName);

                _logger.Information($"[ETransactions] ✅ Alert email queued successfully. QueuedEmailId={queuedId}, Order #{order.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[ETransactions] Failed to send IPN-missing alert email for Order #{order.Id}: {ex.Message}", ex);
        }
    }

    protected virtual async Task<MessageTemplate> EnsureAlertTemplateExistsAsync()
    {
        var template = new MessageTemplate
        {
            Name = ETransactionsPaymentDefaults.IpnMissingMessageTemplateName,
            Subject = "⚠️ ACTION REQUIRED — Unverified payment for Order #%Order.OrderNumber% [%Store.Name%]",
            Body = "<p><strong>⚠️ SECURITY ALERT — ETransactions IPN not received</strong></p>" +
                   "<p>The customer browser returned a <strong>SUCCESS</strong> status from the payment gateway, " +
                   "but the bank's server-to-server IPN notification was <strong>NOT received</strong>.</p>" +
                   "<p><strong>THIS ORDER MUST BE MANUALLY VERIFIED before processing or shipping.</strong></p>" +
                   "<table border='0'>" +
                   "<tr><td><strong>Order:</strong></td><td>#%Order.OrderNumber%</td></tr>" +
                   "<tr><td><strong>Customer:</strong></td><td>%Order.CustomerFullName% (%Order.CustomerEmail%)</td></tr>" +
                   "<tr><td><strong>Amount:</strong></td><td>%Order.OrderTotal%</td></tr>" +
                   "<tr><td><strong>Date:</strong></td><td>%Order.CreatedOn%</td></tr>" +
                   "<tr><td><strong>Store:</strong></td><td>%Store.Name%</td></tr>" +
                   "</table>" +
                   "<br/><p><strong>Possible causes:</strong></p>" +
                   "<ul><li>IPN delivery failed (firewall, timeout, server down at time of payment)</li>" +
                   "<li>Fraudulent browser return manipulation attempt</li></ul>" +
                   "<p><strong>Action required:</strong></p>" +
                   "<ol><li>Log into your Paybox/ETransactions merchant back-office</li>" +
                   "<li>Verify that a real transaction exists for order #%Order.OrderNumber% (amount: %Order.OrderTotal%)</li>" +
                   "<li>If confirmed paid → manually mark the order as Paid in the admin</li>" +
                   "<li>If NOT found → cancel the order immediately</li></ol>",
            IsActive = true,
            EmailAccountId = 0
        };

        await _messageTemplateService.InsertMessageTemplateAsync(template);

        var languages = await _languageService.GetAllLanguagesAsync();
        foreach (var language in languages)
        {
            var culture = language.LanguageCulture?.ToLowerInvariant() ?? string.Empty;
            if (culture.StartsWith("fr"))
            {
                await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Subject,
                    "⚠️ ACTION REQUISE — Paiement non vérifié pour la commande #%Order.OrderNumber% [%Store.Name%]",
                    language.Id);
                await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Body,
                    "<p><strong>⚠️ ALERTE SÉCURITÉ — Notification IPN ETransactions non reçue</strong></p>" +
                    "<p>Le navigateur du client a renvoyé un statut <strong>SUCCÈS</strong> depuis la plateforme de paiement, " +
                    "mais la notification IPN serveur-à-serveur de la banque n'a <strong>PAS été reçue</strong>.</p>" +
                    "<p><strong>CETTE COMMANDE DOIT ÊTRE VÉRIFIÉE MANUELLEMENT avant tout traitement ou expédition.</strong></p>" +
                    "<table border='0'>" +
                    "<tr><td><strong>Commande :</strong></td><td>#%Order.OrderNumber%</td></tr>" +
                    "<tr><td><strong>Client :</strong></td><td>%Order.CustomerFullName% (%Order.CustomerEmail%)</td></tr>" +
                    "<tr><td><strong>Montant :</strong></td><td>%Order.OrderTotal%</td></tr>" +
                    "<tr><td><strong>Date :</strong></td><td>%Order.CreatedOn%</td></tr>" +
                    "<tr><td><strong>Boutique :</strong></td><td>%Store.Name%</td></tr>" +
                    "</table>" +
                    "<br/><p><strong>Causes possibles :</strong></p>" +
                    "<ul><li>Échec de la livraison IPN (pare-feu, délai d'attente, serveur indisponible au moment du paiement)</li>" +
                    "<li>Tentative de manipulation frauduleuse du retour navigateur</li></ul>" +
                    "<p><strong>Actions requises :</strong></p>" +
                    "<ol><li>Connectez-vous à votre back-office Paybox/ETransactions</li>" +
                    "<li>Vérifiez qu'une transaction réelle existe pour la commande #%Order.OrderNumber% (montant : %Order.OrderTotal%)</li>" +
                    "<li>Si confirmé payé → marquez manuellement la commande comme Payée dans l'administration</li>" +
                    "<li>Si introuvable → annulez la commande immédiatement</li></ol>",
                    language.Id);
            }
            else if (culture.StartsWith("en"))
            {
                await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Subject,
                    "⚠️ ACTION REQUIRED — Unverified payment for Order #%Order.OrderNumber% [%Store.Name%]",
                    language.Id);
                await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Body,
                    "<p><strong>⚠️ SECURITY ALERT — ETransactions IPN not received</strong></p>" +
                    "<p>The customer browser returned a <strong>SUCCESS</strong> status from the payment gateway, " +
                    "but the bank's server-to-server IPN notification was <strong>NOT received</strong>.</p>" +
                    "<p><strong>THIS ORDER MUST BE MANUALLY VERIFIED before processing or shipping.</strong></p>" +
                    "<table border='0'>" +
                    "<tr><td><strong>Order:</strong></td><td>#%Order.OrderNumber%</td></tr>" +
                    "<tr><td><strong>Customer:</strong></td><td>%Order.CustomerFullName% (%Order.CustomerEmail%)</td></tr>" +
                    "<tr><td><strong>Amount:</strong></td><td>%Order.OrderTotal%</td></tr>" +
                    "<tr><td><strong>Date:</strong></td><td>%Order.CreatedOn%</td></tr>" +
                    "<tr><td><strong>Store:</strong></td><td>%Store.Name%</td></tr>" +
                    "</table>" +
                    "<br/><p><strong>Possible causes:</strong></p>" +
                    "<ul><li>IPN delivery failed (firewall, timeout, server down at time of payment)</li>" +
                    "<li>Fraudulent browser return manipulation attempt</li></ul>" +
                    "<p><strong>Action required:</strong></p>" +
                    "<ol><li>Log into your Paybox/ETransactions merchant back-office</li>" +
                    "<li>Verify that a real transaction exists for order #%Order.OrderNumber% (amount: %Order.OrderTotal%)</li>" +
                    "<li>If confirmed paid → manually mark the order as Paid in the admin</li>" +
                    "<li>If NOT found → cancel the order immediately</li></ol>",
                    language.Id);
            }
        }

        _logger.Information($"[ETransactions] Alert template '{ETransactionsPaymentDefaults.IpnMissingMessageTemplateName}' created automatically.");
        return template;
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
