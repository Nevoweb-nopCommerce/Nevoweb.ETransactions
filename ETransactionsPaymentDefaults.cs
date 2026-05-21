namespace Nevoweb.ETransactions;

public static class ETransactionsPaymentDefaults
{
    public const string SystemName = "Payments.ETransactions";

    public const string IpnMissingMessageTemplateName = "ETransactions.IpnMissingFraudAlert";

    /// <summary>
    /// Filename of the RSA-2048 public key used for production IPN verification.
    /// File must exist at: Plugins/Payments.ETransactions/etc/pubkey_RSA_2048.pem
    /// Source: PHP module download at https://www.ca-moncommerce.com/espace-client-mon-commerce/up2pay-e-transactions/telecharger-mes-modules/php/
    /// </summary>
    public const string IpnPublicKeyProductionFile = "pubkey_RSA_2048.pem";

    /// <summary>
    /// Filename of the RSA-1024 public key used for pre-production IPN verification.
    /// File must exist at: Plugins/Payments.ETransactions/etc/pubkey.pem
    /// Source: PHP module download at https://www.ca-moncommerce.com/espace-client-mon-commerce/up2pay-e-transactions/telecharger-mes-modules/php/
    /// </summary>
    public const string IpnPublicKeyPreproductionFile = "pubkey.pem";

    public static class Route
    {
        public const string Configure = "Plugin.Payments.ETransactions.Configure";
        public const string Redirect = "Plugin.Payments.ETransactions.Redirect";
        public const string Return = "Plugin.Payments.ETransactions.Return";
        public const string Notify = "Plugin.Payments.ETransactions.Notify";
        public const string HomePage = "Homepage";
        public const string CheckoutCompleted = "CheckoutCompleted";
        public const string ShoppingCart = "ShoppingCart";
    }
}
