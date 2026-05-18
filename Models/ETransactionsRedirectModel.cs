namespace Nevoweb.ETransactions.Models;

public record ETransactionsRedirectModel
{
    public string PostUrl { get; set; }
    public IDictionary<string, string> Inputs { get; set; }
}
