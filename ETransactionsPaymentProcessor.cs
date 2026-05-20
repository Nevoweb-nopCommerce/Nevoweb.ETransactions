using Microsoft.AspNetCore.Http;
using Nevoweb.ETransactions.Components;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nevoweb.ETransactions;

public class ETransactionsPaymentProcessor : BasePlugin, IPaymentMethod
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILanguageService _languageService;
    private readonly ILocalizedEntityService _localizedEntityService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IWebHelper _webHelper;
    private readonly ETransactionsPaymentSettings _settings;

    public ETransactionsPaymentProcessor(IHttpContextAccessor httpContextAccessor,
        ILanguageService languageService,
        ILocalizedEntityService localizedEntityService,
        ILocalizationService localizationService,
        IMessageTemplateService messageTemplateService,
        IOrderTotalCalculationService orderTotalCalculationService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IWebHelper webHelper,
        ETransactionsPaymentSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _languageService = languageService;
        _localizedEntityService = localizedEntityService;
        _localizationService = localizationService;
        _messageTemplateService = messageTemplateService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _webHelper = webHelper;
        _settings = settings;
    }

    public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        return Task.FromResult(new ProcessPaymentResult
        {
            NewPaymentStatus = PaymentStatus.Pending
        });
    }

    public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
    {
        ArgumentNullException.ThrowIfNull(postProcessPaymentRequest?.Order);

        var url = $"{_webHelper.GetStoreLocation()}Plugins/ETransactions/Redirect/{postProcessPaymentRequest.Order.Id}";
        _httpContextAccessor.HttpContext?.Response.Redirect(url, false);

        return Task.CompletedTask;
    }

    public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        if (_settings.ShippableProductRequired && !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return true;

        return false;
    }

    public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
    {
        return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart, _settings.AdditionalFee, _settings.AdditionalFeePercentage);
    }

    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        => Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });

    public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        => Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });

    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        => Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });

    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        => Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });

    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        => Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });

    public Task<bool> CanRePostProcessPaymentAsync(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Task.FromResult(order.PaymentStatus == PaymentStatus.Pending && (DateTime.UtcNow - order.CreatedOnUtc).TotalHours < 24);
    }

    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        => Task.FromResult<IList<string>>(new List<string>());

    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        => Task.FromResult(new ProcessPaymentRequest());

    public override string GetConfigurationPageUrl()
        => $"{_webHelper.GetStoreLocation()}Admin/ETransactions/Configure";

    public Type GetPublicViewComponent()
        => typeof(ETransactionsPaymentInfoViewComponent);

    public override async Task InstallAsync()
    {
        var settings = new ETransactionsPaymentSettings
        {
            DescriptionText = "You will be redirected to the ETransactions / Up2Pay payment page to complete your order.",
            PbxSite = "1999888",
            PbxRang = "32",
            PbxDevise = "978",
            PbxIdentifiant = "107904482",
            PbxRetour = "ref:R;rtnerr:E;auto:A;trans:S;call:T",
            HmacKey = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            MainUrl = "https://tpeweb.paybox.com/cgi/MYchoix_pagepaiement.cgi",
            BackupUrl = "https://tpeweb1.paybox.com/cgi/MYchoix_pagepaiement.cgi",
            PreprodUrl = "https://preprod-tpeweb.paybox.com/cgi/MYchoix_pagepaiement.cgi"
        };

        await _settingService.SaveSettingAsync(settings);

        // Create fraud alert message template if it doesn't exist
        var existing = await _messageTemplateService.GetMessageTemplatesByNameAsync(ETransactionsPaymentDefaults.IpnMissingMessageTemplateName);
        if (!existing.Any())
        {
            var template = new MessageTemplate
            {
                Name = ETransactionsPaymentDefaults.IpnMissingMessageTemplateName,
                Subject = "ACTION REQUIRED — Unverified payment for Order #%Order.OrderNumber% [%Store.Name%]",
                Body = "<p><strong>SECURITY ALERT — ETransactions IPN not received</strong></p>" +
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

            // Save per-language localized Subject + Body
            var languages = await _languageService.GetAllLanguagesAsync();

            foreach (var language in languages)
            {
                var culture = language.LanguageCulture?.ToLowerInvariant() ?? string.Empty;

                if (culture.StartsWith("fr"))
                {
                    await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Subject,
                        "ACTION REQUISE — Paiement non vérifié pour la commande #%Order.OrderNumber% [%Store.Name%]",
                        language.Id);
                    await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Body,
                        "<p><strong>ALERTE SÉCURITÉ — Notification IPN ETransactions non reçue</strong></p>" +
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
                    // English is the default template body — save explicitly so it is always set
                    await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Subject,
                        "ACTION REQUIRED — Unverified payment for Order #%Order.OrderNumber% [%Store.Name%]",
                        language.Id);
                    await _localizedEntityService.SaveLocalizedValueAsync(template, t => t.Body,
                        "<p><strong>SECURITY ALERT — ETransactions IPN not received</strong></p>" +
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
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Payment.ETransactions.PaymentMethodDescription"] = "Pay securely via ETransactions / Up2Pay",
            ["Plugins.Payment.ETransactions.DescriptionText"] = "Checkout description",
            ["Plugins.Payment.ETransactions.DescriptionText.Hint"] = "Text shown on the payment info step.",
            ["Plugins.Payment.ETransactions.AdditionalFee"] = "Additional fee",
            ["Plugins.Payment.ETransactions.AdditionalFee.Hint"] = "Optional additional fee for this payment method.",
            ["Plugins.Payment.ETransactions.AdditionalFeePercentage"] = "Additional fee. Use percentage",
            ["Plugins.Payment.ETransactions.AdditionalFeePercentage.Hint"] = "Use percentage for additional fee instead of fixed amount.",
            ["Plugins.Payment.ETransactions.ShippableProductRequired"] = "Shippable product required",
            ["Plugins.Payment.ETransactions.ShippableProductRequired.Hint"] = "Display this payment method only when cart requires shipping.",
            ["Plugins.Payment.ETransactions.PbxSite"] = "PBX_SITE",
            ["Plugins.Payment.ETransactions.PbxRang"] = "PBX_RANG",
            ["Plugins.Payment.ETransactions.PbxDevise"] = "PBX_DEVISE",
            ["Plugins.Payment.ETransactions.PbxIdentifiant"] = "PBX_IDENTIFIANT",
            ["Plugins.Payment.ETransactions.PbxRetour"] = "PBX_RETOUR",
            ["Plugins.Payment.ETransactions.ReserveOnly"] = "Reserve only (PBX_AUTOSEULE)",
            ["Plugins.Payment.ETransactions.HmacKey"] = "HMAC key",
            ["Plugins.Payment.ETransactions.MainUrl"] = "Main URL",
            ["Plugins.Payment.ETransactions.BackupUrl"] = "Backup URL",
            ["Plugins.Payment.ETransactions.PreprodUrl"] = "Pre-production URL",
            ["Plugins.Payment.ETransactions.Preproduction"] = "Pre-production mode",
            ["Plugins.Payment.ETransactions.DebugMode"] = "Debug mode",
            ["Plugins.Payment.ETransactions.ValidateSourceIp"] = "Validate callback source IP",
            ["Plugins.Payment.ETransactions.AllowedIps"] = "Allowed callback IPs",
            ["Plugins.Payment.ETransactions.AllowedIps.Hint"] = "Comma separated list of IPs allowed for callback notifications.",
            ["Plugins.Payment.ETransactions.PaymentFailed"] = "The payment was refused or canceled.",
            ["Plugins.Payment.ETransactions.PaymentPending"] = "Your payment is being validated by ETransactions / Up2Pay."
        });

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<ETransactionsPaymentSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payment.ETransactions");

        // Remove fraud alert message template
        var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(ETransactionsPaymentDefaults.IpnMissingMessageTemplateName);
        foreach (var template in templates)
            await _messageTemplateService.DeleteMessageTemplateAsync(template);

        await base.UninstallAsync();
    }

    public async Task<string> GetPaymentMethodDescriptionAsync()
        => await _localizationService.GetResourceAsync("Plugins.Payment.ETransactions.PaymentMethodDescription");

    public bool SupportCapture => false;
    public bool SupportPartiallyRefund => false;
    public bool SupportRefund => false;
    public bool SupportVoid => false;
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;
    public bool SkipPaymentInfo => false;
}
