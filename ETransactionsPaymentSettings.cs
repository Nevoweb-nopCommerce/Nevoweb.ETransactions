using Nop.Core.Configuration;

namespace Nevoweb.ETransactions;

public class ETransactionsPaymentSettings : ISettings
{
    public string DescriptionText { get; set; }
    public decimal AdditionalFee { get; set; }
    public bool AdditionalFeePercentage { get; set; }
    public bool ShippableProductRequired { get; set; }

    public string PbxSite { get; set; }
    public string PbxRang { get; set; }
    public string PbxDevise { get; set; }
    public string PbxIdentifiant { get; set; }
    public string PbxRetour { get; set; }
    public bool ReserveOnly { get; set; }
    public string HmacKey { get; set; }

    public string MainUrl { get; set; }
    public string BackupUrl { get; set; }
    public string PreprodUrl { get; set; }
    public bool Preproduction { get; set; }

    public bool DebugMode { get; set; }
    public bool ValidateSourceIp { get; set; }
    public string AllowedIps { get; set; }

    /// <summary>
    /// When true (default), the RSA-SHA1 signature from the gateway IPN 'sign' parameter is verified.
    /// Disable only for troubleshooting — never in production.
    /// </summary>
    public bool ValidateRsaSignature { get; set; } = true;
}
