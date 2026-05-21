using Microsoft.AspNetCore.Mvc;
using Nevoweb.ETransactions.Models;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nevoweb.ETransactions.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class PaymentETransactionsController : BasePaymentController
{
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    public PaymentETransactionsController(INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<ETransactionsPaymentSettings>(storeScope);

        var model = new ConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            DescriptionText = settings.DescriptionText,
            AdditionalFee = settings.AdditionalFee,
            AdditionalFeePercentage = settings.AdditionalFeePercentage,
            ShippableProductRequired = settings.ShippableProductRequired,
            PbxSite = settings.PbxSite,
            PbxRang = settings.PbxRang,
            PbxDevise = settings.PbxDevise,
            PbxIdentifiant = settings.PbxIdentifiant,
            PbxRetour = settings.PbxRetour,
            ReserveOnly = settings.ReserveOnly,
            HmacKey = settings.HmacKey,
            MainUrl = settings.MainUrl,
            BackupUrl = settings.BackupUrl,
            PreprodUrl = settings.PreprodUrl,
            Preproduction = settings.Preproduction,
            DebugMode = settings.DebugMode,
            ValidateSourceIp = settings.ValidateSourceIp,
            AllowedIps = settings.AllowedIps,
            ValidateRsaSignature = settings.ValidateRsaSignature
        };

        if (storeScope > 0)
        {
            model.DescriptionText_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.DescriptionText, storeScope);
            model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AdditionalFee, storeScope);
            model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AdditionalFeePercentage, storeScope);
            model.ShippableProductRequired_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ShippableProductRequired, storeScope);
            model.PbxSite_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PbxSite, storeScope);
            model.PbxRang_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PbxRang, storeScope);
            model.PbxDevise_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PbxDevise, storeScope);
            model.PbxIdentifiant_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PbxIdentifiant, storeScope);
            model.PbxRetour_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PbxRetour, storeScope);
            model.ReserveOnly_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ReserveOnly, storeScope);
            model.HmacKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.HmacKey, storeScope);
            model.MainUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.MainUrl, storeScope);
            model.BackupUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.BackupUrl, storeScope);
            model.PreprodUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PreprodUrl, storeScope);
            model.Preproduction_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Preproduction, storeScope);
            model.DebugMode_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.DebugMode, storeScope);
            model.ValidateSourceIp_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ValidateSourceIp, storeScope);
            model.AllowedIps_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AllowedIps, storeScope);
            model.ValidateRsaSignature_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ValidateRsaSignature, storeScope);
        }

        return View("~/Plugins/Payments.ETransactions/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<ETransactionsPaymentSettings>(storeScope);

        settings.DescriptionText = model.DescriptionText;
        settings.AdditionalFee = model.AdditionalFee;
        settings.AdditionalFeePercentage = model.AdditionalFeePercentage;
        settings.ShippableProductRequired = model.ShippableProductRequired;
        settings.PbxSite = model.PbxSite;
        settings.PbxRang = model.PbxRang;
        settings.PbxDevise = model.PbxDevise;
        settings.PbxIdentifiant = model.PbxIdentifiant;
        settings.PbxRetour = model.PbxRetour;
        settings.ReserveOnly = model.ReserveOnly;
        settings.HmacKey = model.HmacKey;
        settings.MainUrl = model.MainUrl;
        settings.BackupUrl = model.BackupUrl;
        settings.PreprodUrl = model.PreprodUrl;
        settings.Preproduction = model.Preproduction;
        settings.DebugMode = model.DebugMode;
        settings.ValidateSourceIp = model.ValidateSourceIp;
        settings.AllowedIps = model.AllowedIps;
        settings.ValidateRsaSignature = model.ValidateRsaSignature;

        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.DescriptionText, model.DescriptionText_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ShippableProductRequired, model.ShippableProductRequired_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PbxSite, model.PbxSite_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PbxRang, model.PbxRang_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PbxDevise, model.PbxDevise_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PbxIdentifiant, model.PbxIdentifiant_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PbxRetour, model.PbxRetour_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ReserveOnly, model.ReserveOnly_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.HmacKey, model.HmacKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.MainUrl, model.MainUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.BackupUrl, model.BackupUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PreprodUrl, model.PreprodUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Preproduction, model.Preproduction_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.DebugMode, model.DebugMode_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ValidateSourceIp, model.ValidateSourceIp_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AllowedIps, model.AllowedIps_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ValidateRsaSignature, model.ValidateRsaSignature_OverrideForStore, storeScope, false);

        await _settingService.ClearCacheAsync();
        _notificationService.SuccessNotification("Saved");

        return await Configure();
    }
}
