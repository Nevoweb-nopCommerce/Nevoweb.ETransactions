using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nevoweb.ETransactions.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.DescriptionText")]
    public string DescriptionText { get; set; }
    public bool DescriptionText_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.AdditionalFee")]
    public decimal AdditionalFee { get; set; }
    public bool AdditionalFee_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }
    public bool AdditionalFeePercentage_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.ShippableProductRequired")]
    public bool ShippableProductRequired { get; set; }
    public bool ShippableProductRequired_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PbxSite")]
    public string PbxSite { get; set; }
    public bool PbxSite_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PbxRang")]
    public string PbxRang { get; set; }
    public bool PbxRang_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PbxDevise")]
    public string PbxDevise { get; set; }
    public bool PbxDevise_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PbxIdentifiant")]
    public string PbxIdentifiant { get; set; }
    public bool PbxIdentifiant_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PbxRetour")]
    public string PbxRetour { get; set; }
    public bool PbxRetour_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.ReserveOnly")]
    public bool ReserveOnly { get; set; }
    public bool ReserveOnly_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.HmacKey")]
    public string HmacKey { get; set; }
    public bool HmacKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.MainUrl")]
    public string MainUrl { get; set; }
    public bool MainUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.BackupUrl")]
    public string BackupUrl { get; set; }
    public bool BackupUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.PreprodUrl")]
    public string PreprodUrl { get; set; }
    public bool PreprodUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.Preproduction")]
    public bool Preproduction { get; set; }
    public bool Preproduction_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.DebugMode")]
    public bool DebugMode { get; set; }
    public bool DebugMode_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.ValidateSourceIp")]
    public bool ValidateSourceIp { get; set; }
    public bool ValidateSourceIp_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.AllowedIps")]
    public string AllowedIps { get; set; }
    public bool AllowedIps_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.ETransactions.ValidateRsaSignature")]
    public bool ValidateRsaSignature { get; set; }
    public bool ValidateRsaSignature_OverrideForStore { get; set; }
}
