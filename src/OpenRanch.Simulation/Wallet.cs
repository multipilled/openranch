namespace OpenRanch.Simulation;

/// <summary>The player's coins (newbucks): what the plort market pays goes here.</summary>
public sealed class Wallet
{
    public Wallet(int coins = 0) => Coins = coins;

    public int Coins { get; private set; }

    /// <summary>Raised after every change, with the amount added (negative when spent).</summary>
    public event Action<int>? Changed;

    public void Add(int amount)
    {
        if (amount <= 0)
            return;
        Coins += amount;
        Changed?.Invoke(amount);
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Coins)
            return false;
        Coins -= amount;
        Changed?.Invoke(-amount);
        return true;
    }
}
