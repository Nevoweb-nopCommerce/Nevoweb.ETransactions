namespace Nevoweb.ETransactions;

public static class ETransactionsPaymentDefaults
{
    public const string SystemName = "Payments.ETransactions";

    public const string IpnMissingMessageTemplateName = "ETransactions.IpnMissingFraudAlert";

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
