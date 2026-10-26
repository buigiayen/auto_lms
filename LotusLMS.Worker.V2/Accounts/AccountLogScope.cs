namespace LotusLMS.Worker.V2.Accounts;

// Only these non-secret ownership fields are rendered by the console formatter.
internal sealed record AccountLogScope(string Lane, string Account, string Run, int? Plan)
{
    public override string ToString() => $"[lane={Lane} account={Account} run={Run} plan={Plan}] ";
}
