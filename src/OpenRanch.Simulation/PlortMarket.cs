using OpenRanch.Formats.Game;

namespace OpenRanch.Simulation;

/// <summary>
/// The day-to-day drift of plort prices: one factor for the whole market and one per plort. See
/// docs/behavior/plort-market.md, "Daily mood".
/// </summary>
public interface IMarketMood
{
    float Market(int day);
    float Item(int day, string id);
}

/// <summary>A mood that never moves prices (factor 1), for tests and for reading prices off the data.</summary>
public sealed class NeutralMood : IMarketMood
{
    public static readonly NeutralMood Instance = new();
    public float Market(int day) => 1f;
    public float Item(int day, string id) => 1f;
}

/// <summary>
/// openranch's own smooth random mood: values wander between <see cref="Low"/> and <see cref="High"/>,
/// changing gradually over about <see cref="PeriodDays"/> days, the same spread as the original.
/// It is not the original's noise function, so exact prices on a given day differ.
/// </summary>
public sealed class WanderingMood : IMarketMood
{
    // The spread of the original's daily factors (centre 0.7, swing 0.6). See docs/behavior/plort-market.md.
    public const float Centre = 0.7f;
    public const float Swing = 0.6f;
    public const float Low = Centre - Swing;
    public const float High = Centre + Swing;
    public const int PeriodDays = 10;

    private readonly int _seed;

    public WanderingMood(int seed) => _seed = seed;

    public float Market(int day) => Centre + Swing * Smooth(day, 0x5EED);
    public float Item(int day, string id) => Centre + Swing * Smooth(day, StableHash(id));

    // Value noise: a random value in [-1, 1] every PeriodDays days, eased between them.
    private float Smooth(int day, int channel)
    {
        var cell = (int)Math.Floor(day / (double)PeriodDays);
        var t = (day - cell * PeriodDays) / (float)PeriodDays;
        t = t * t * (3 - 2 * t);
        var a = Lattice(cell, channel);
        var b = Lattice(cell + 1, channel);
        return a + (b - a) * t;
    }

    private float Lattice(int cell, int channel)
    {
        unchecked
        {
            var h = (uint)(_seed * 73856093 ^ cell * 19349663 ^ channel * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h / (float)uint.MaxValue * 2f - 1f;
        }
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s)
                h = (h ^ c) * 16777619;
            return h;
        }
    }
}

/// <summary>
/// The plort market: prices from the install's base values, lowered by how many of each plort have
/// been sold lately (saturation) and recalculated once a day. See docs/behavior/plort-market.md.
/// </summary>
public sealed class PlortMarket
{
    /// <summary>A new game starts every plort at this fraction of its full saturation.</summary>
    public const float StartingSaturationFraction = 0.5f;
    /// <summary>Prices in game modes without a moving market are base value times this.</summary>
    public const float FixedPriceFactor = 1.5f;

    private sealed class Entry
    {
        public required MarketEntryData Data;
        public float Saturation;
        public float Price;
        public float PreviousPrice;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private IMarketMood _mood;
    private bool _firstUpdateDone;

    public PlortMarket(MarketData data, IMarketMood? mood = null, bool dynamic = true)
    {
        Data = data;
        Dynamic = dynamic;
        _mood = mood ?? NeutralMood.Instance;
        foreach (var e in data.Entries)
            _entries[e.Id] = new Entry { Data = e, Saturation = e.FullSaturation * StartingSaturationFraction, Price = e.BaseValue, PreviousPrice = e.BaseValue };
        StartDay(0);
    }

    public MarketData Data { get; }
    public bool Dynamic { get; }
    public int Day { get; private set; }

    public IEnumerable<string> Accepted => _entries.Keys;
    public bool Accepts(string id) => _entries.ContainsKey(id);

    /// <summary>Today's price, in whole coins, or null if the market doesn't buy it.</summary>
    public int? Price(string id) => _entries.TryGetValue(id, out var e) ? Round(e.Price) : null;

    /// <summary>Today's price minus yesterday's, as the market board shows it.</summary>
    public int? PriceChange(string id) => _entries.TryGetValue(id, out var e) ? Round(e.Price) - Round(e.PreviousPrice) : null;

    public float Saturation(string id) => _entries[id].Saturation;

    public void SetSaturation(string id, float value) => _entries[id].Saturation = value;

    /// <summary>Every plort's saturation now, by item id.</summary>
    public IReadOnlyDictionary<string, float> Saturations => _entries.ToDictionary(e => e.Key, e => e.Value.Saturation);

    /// <summary>
    /// Takes a saved market: the saturation of each plort it buys (others keep theirs) and, if given,
    /// the mood drawn from the save's seed. Prices follow at <see cref="Open"/>.
    /// </summary>
    public void Load(IReadOnlyDictionary<string, float> saturation, IMarketMood? mood = null)
    {
        foreach (var (id, value) in saturation)
            if (_entries.TryGetValue(id, out var e))
                e.Saturation = value;
        if (mood is not null)
            _mood = mood;
    }

    /// <summary>
    /// The market's first update once a game is opened: today's prices for <paramref name="day"/> from
    /// the saturation as it stands, with no recovery, and the base values as yesterday's prices (static
    /// analysis of EconomyDirector: its first update after the level loads skips the recovery step).
    /// </summary>
    public void Open(int day)
    {
        Day = day;
        foreach (var (id, e) in _entries)
        {
            e.PreviousPrice = e.Data.BaseValue;
            e.Price = TargetPrice(id, e, day);
        }
        _firstUpdateDone = true;
    }

    /// <summary>The market is closed for the first few minutes after midnight while prices change.</summary>
    public bool IsClosed(float hourOfDay) => Dynamic && hourOfDay * 60f < Data.DailyShutdownMinutes;

    /// <summary>
    /// Sells <paramref name="count"/> of <paramref name="id"/> at today's price and returns the coins
    /// paid, or null if the market doesn't buy it. The sale counts toward saturation, which lowers the
    /// price from the next day's update on, not straight away.
    /// </summary>
    public int? Sell(string id, int count = 1)
    {
        if (count <= 0 || !_entries.TryGetValue(id, out var e))
            return null;
        var paid = Round(e.Price) * count;
        e.Saturation += count;
        return paid;
    }

    /// <summary>
    /// Midnight: saturation recovers by the install's recovery fraction (except at the very first
    /// update of a new game) and every price is recalculated for <paramref name="day"/>.
    /// </summary>
    public void StartDay(int day)
    {
        Day = day;
        foreach (var (id, e) in _entries)
        {
            if (_firstUpdateDone)
                e.Saturation *= 1f - Data.SaturationRecovery;
            e.PreviousPrice = e.Price;
            e.Price = TargetPrice(id, e, day);
        }
        _firstUpdateDone = true;
    }

    /// <summary>How strongly demand lifts the base price: 2 with nothing sold, 1 at full saturation or beyond.</summary>
    public static float Demand(float saturation, float fullSaturation) =>
        1f + Math.Clamp((fullSaturation - saturation) / fullSaturation, 0f, 1f);

    private float TargetPrice(string id, Entry e, int day)
    {
        if (!Dynamic)
            return e.Data.BaseValue * FixedPriceFactor;
        return e.Data.BaseValue * Demand(e.Saturation, e.Data.FullSaturation) * _mood.Market(day) * _mood.Item(day, id);
    }

    // Prices round to the nearest coin, halves to the even coin, as the original does.
    private static int Round(float value) => (int)Math.Round(value, MidpointRounding.ToEven);
}
