// RubiKit Trading Analyzer
// Provides market analysis, GMS arbitrage detection, and shop management insights

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RubiKit.LLM.Providers
{
    /// <summary>
    /// Trading analysis provider - GMS arbitrage, shop builder, market trends
    /// </summary>
    public sealed class TradingAnalyzer : AnalysisProviderBase
    {
        public override string Id => "trading";
        public override string Name => "Trading Analyzer";
        public override string Domain => "trading";
        public override int Priority => 80;

        // Configuration
        public bool EnableArbitrageAlerts { get; set; } = true;
        public bool EnablePriceTracking { get; set; } = true;
        public bool EnableShopAnalysis { get; set; } = true;
        public double ArbitrageThresholdPercent { get; set; } = 10.0; // Alert when spread > 10%
        public long MinProfitThreshold { get; set; } = 100000; // 100k minimum profit

        // Tracking
        private readonly Dictionary<string, PriceHistory> _priceHistories = new Dictionary<string, PriceHistory>();
        private readonly Dictionary<string, long> _lastKnownPrices = new Dictionary<string, long>();
        private readonly List<ArbitrageOpportunity> _activeOpportunities = new List<ArbitrageOpportunity>();
        private DateTime _lastMarketScan = DateTime.MinValue;

        // Shop tracking
        private readonly Dictionary<string, ShopItem> _shopInventory = new Dictionary<string, ShopItem>();
        private long _shopTotalValue = 0;
        private int _shopSalesCount = 0;
        private long _shopRevenue = 0;

        public override void Initialize(ContextEngine context, LLMService llm)
        {
            base.Initialize(context, llm);

            // Register trading event triggers
            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "shop_sale",
                EventType = GameEventType.ShopSale,
                Domain = "trading",
                CooldownMs = 0, // No cooldown for sales
                OnTrigger = (evt, ctx) => HandleShopSale(evt)
            });

            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "gms_trade",
                EventType = GameEventType.GMSTrade,
                Domain = "trading",
                CooldownMs = 1000,
                OnTrigger = (evt, ctx) => HandleGMSTrade(evt)
            });

            context.RegisterTrigger(new AnalysisTrigger
            {
                Name = "price_alert",
                EventType = GameEventType.PriceAlert,
                Domain = "trading",
                CooldownMs = 5000,
                OnTrigger = (evt, ctx) => HandlePriceAlert(evt)
            });
        }

        public override void OnEvent(GameEvent evt)
        {
            switch (evt.Type)
            {
                case GameEventType.ShopSale:
                    HandleShopSale(evt);
                    break;

                case GameEventType.ShopPurchase:
                    HandleShopPurchase(evt);
                    break;

                case GameEventType.GMSTrade:
                    HandleGMSTrade(evt);
                    break;

                case GameEventType.PriceAlert:
                    HandlePriceAlert(evt);
                    break;
            }
        }

        private void HandleShopSale(GameEvent evt)
        {
            var itemName = evt.Data.TryGetValue("item", out var i) ? i?.ToString() : "Unknown";
            var price = evt.Data.TryGetValue("price", out var p) ? Convert.ToInt64(p) : 0;
            var quantity = evt.Data.TryGetValue("quantity", out var q) ? Convert.ToInt32(q) : 1;

            _shopSalesCount++;
            _shopRevenue += price * quantity;

            // Update inventory
            if (_shopInventory.TryGetValue(itemName, out var item))
            {
                item.Quantity -= quantity;
                if (item.Quantity <= 0)
                    _shopInventory.Remove(itemName);
            }

            // Update price history
            UpdatePriceHistory(itemName, price);

            // Calculate profit if we know cost basis
            var profitStr = "";
            if (_lastKnownPrices.TryGetValue(itemName, out var costBasis))
            {
                var profit = price - costBasis;
                var profitPct = costBasis > 0 ? (double)profit / costBasis * 100 : 0;
                profitStr = profit >= 0 ? $" (+{profit:N0}, {profitPct:N1}%)" : $" ({profit:N0}, {profitPct:N1}%)";
            }

            AddCallout($"SALE: {itemName} x{quantity} @ {price:N0}{profitStr}", CalloutType.Trade, 4000);

            // Check if this affects arbitrage opportunities
            CheckArbitrageUpdate(itemName, price);
        }

        private void HandleShopPurchase(GameEvent evt)
        {
            var itemName = evt.Data.TryGetValue("item", out var i) ? i?.ToString() : "Unknown";
            var price = evt.Data.TryGetValue("price", out var p) ? Convert.ToInt64(p) : 0;
            var quantity = evt.Data.TryGetValue("quantity", out var q) ? Convert.ToInt32(q) : 1;

            // Track cost basis
            _lastKnownPrices[itemName] = price;

            // Update inventory
            if (!_shopInventory.TryGetValue(itemName, out var item))
            {
                item = new ShopItem { Name = itemName };
                _shopInventory[itemName] = item;
            }
            item.Quantity += quantity;
            item.CostBasis = price;
            item.LastUpdate = DateTime.UtcNow;

            UpdatePriceHistory(itemName, price);

            AddCallout($"Purchased: {itemName} x{quantity} @ {price:N0}", CalloutType.Trade, 3000);
        }

        private void HandleGMSTrade(GameEvent evt)
        {
            var itemName = evt.Data.TryGetValue("item", out var i) ? i?.ToString() : "Unknown";
            var buyPrice = evt.Data.TryGetValue("buyPrice", out var bp) ? Convert.ToInt64(bp) : 0;
            var sellPrice = evt.Data.TryGetValue("sellPrice", out var sp) ? Convert.ToInt64(sp) : 0;
            var volume = evt.Data.TryGetValue("volume", out var v) ? Convert.ToInt32(v) : 0;

            // Update price history
            UpdatePriceHistory(itemName, (buyPrice + sellPrice) / 2);

            // Check for arbitrage
            if (EnableArbitrageAlerts)
            {
                CheckArbitrageOpportunity(itemName, buyPrice, sellPrice, volume);
            }
        }

        private void HandlePriceAlert(GameEvent evt)
        {
            var itemName = evt.Data.TryGetValue("item", out var i) ? i?.ToString() : "Unknown";
            var price = evt.Data.TryGetValue("price", out var p) ? Convert.ToInt64(p) : 0;
            var condition = evt.Data.TryGetValue("condition", out var c) ? c?.ToString() : "";

            AddCallout($"PRICE ALERT: {itemName} {condition} @ {price:N0}", CalloutType.Warning, 6000);
        }

        private void UpdatePriceHistory(string itemName, long price)
        {
            if (!_priceHistories.TryGetValue(itemName, out var history))
            {
                history = new PriceHistory { ItemName = itemName };
                _priceHistories[itemName] = history;
            }

            history.AddPrice(price);
        }

        private void CheckArbitrageOpportunity(string itemName, long buyPrice, long sellPrice, int volume)
        {
            if (sellPrice <= 0 || buyPrice <= 0) return;

            var spread = (double)(sellPrice - buyPrice) / buyPrice * 100;
            var potentialProfit = (sellPrice - buyPrice) * Math.Min(volume, 10); // Assume max 10 units

            if (spread >= ArbitrageThresholdPercent && potentialProfit >= MinProfitThreshold)
            {
                var opp = new ArbitrageOpportunity
                {
                    ItemName = itemName,
                    BuyPrice = buyPrice,
                    SellPrice = sellPrice,
                    SpreadPercent = spread,
                    PotentialProfit = potentialProfit,
                    Volume = volume,
                    Timestamp = DateTime.UtcNow
                };

                // Check if this is a new or improved opportunity
                var existing = _activeOpportunities.FirstOrDefault(o => o.ItemName == itemName);
                if (existing == null || opp.SpreadPercent > existing.SpreadPercent)
                {
                    if (existing != null)
                        _activeOpportunities.Remove(existing);
                    _activeOpportunities.Add(opp);

                    // Alert!
                    AddCallout($"ARBITRAGE: {itemName} spread {spread:N1}% (+{potentialProfit:N0} potential)",
                        CalloutType.Trade, 8000);

                    // Request LLM analysis for significant opportunities
                    if (potentialProfit >= MinProfitThreshold * 5 && LLM != null && LLM.Config.Enabled)
                    {
                        var context = GetContext();
                        var prompt = $"Arbitrage opportunity detected: {itemName} with {spread:N1}% spread. " +
                                    $"Buy at {buyPrice:N0}, sell at {sellPrice:N0}. Volume: {volume}. " +
                                    $"Analyze risk and provide recommendation (under 50 words).";

                        LLM.AnalyzeAsync(context + "\n\n" + prompt, "trading", response =>
                        {
                            AddCallout(response, CalloutType.Tip, 10000);
                        });
                    }
                }
            }

            // Cleanup old opportunities
            _activeOpportunities.RemoveAll(o => (DateTime.UtcNow - o.Timestamp).TotalMinutes > 30);
        }

        private void CheckArbitrageUpdate(string itemName, long salePrice)
        {
            var opp = _activeOpportunities.FirstOrDefault(o => o.ItemName == itemName);
            if (opp != null)
            {
                // Update or remove opportunity based on new sale
                if (salePrice <= opp.BuyPrice)
                {
                    AddCallout($"Arbitrage closing: {itemName} price dropped to {salePrice:N0}", CalloutType.Warning, 5000);
                    _activeOpportunities.Remove(opp);
                }
            }
        }

        public override async Task<IEnumerable<AnalysisResult>> AnalyzeAsync()
        {
            var results = new List<AnalysisResult>();

            // Shop summary
            if (EnableShopAnalysis && _shopInventory.Count > 0)
            {
                _shopTotalValue = _shopInventory.Values.Sum(i => i.CostBasis * i.Quantity);

                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "Shop Inventory",
                    Content = $"{_shopInventory.Count} items, {_shopTotalValue:N0} credits total value",
                    Severity = AnalysisSeverity.Info,
                    Metadata = {
                        { "itemCount", _shopInventory.Count },
                        { "totalValue", _shopTotalValue },
                        { "salesCount", _shopSalesCount },
                        { "revenue", _shopRevenue }
                    }
                });
            }

            // Active arbitrage opportunities
            if (EnableArbitrageAlerts && _activeOpportunities.Count > 0)
            {
                foreach (var opp in _activeOpportunities.OrderByDescending(o => o.PotentialProfit).Take(3))
                {
                    results.Add(new AnalysisResult
                    {
                        ProviderId = Id,
                        Title = $"Arbitrage: {opp.ItemName}",
                        Content = $"Spread: {opp.SpreadPercent:N1}%, Profit: {opp.PotentialProfit:N0}",
                        Severity = AnalysisSeverity.Opportunity,
                        Metadata = {
                            { "item", opp.ItemName },
                            { "buyPrice", opp.BuyPrice },
                            { "sellPrice", opp.SellPrice },
                            { "spread", opp.SpreadPercent },
                            { "profit", opp.PotentialProfit }
                        }
                    });
                }
            }

            // Price trend analysis for tracked items
            if (EnablePriceTracking)
            {
                foreach (var history in _priceHistories.Values.Where(h => h.HasTrend))
                {
                    var trend = history.GetTrend();
                    if (Math.Abs(trend.changePercent) >= 5) // 5% change threshold
                    {
                        var direction = trend.changePercent > 0 ? "UP" : "DOWN";
                        var severity = Math.Abs(trend.changePercent) >= 15
                            ? AnalysisSeverity.Warning
                            : AnalysisSeverity.Info;

                        results.Add(new AnalysisResult
                        {
                            ProviderId = Id,
                            Title = $"Price Trend: {history.ItemName}",
                            Content = $"{direction} {Math.Abs(trend.changePercent):N1}% ({trend.oldPrice:N0} → {trend.newPrice:N0})",
                            Severity = severity,
                            Metadata = {
                                { "item", history.ItemName },
                                { "change", trend.changePercent },
                                { "oldPrice", trend.oldPrice },
                                { "newPrice", trend.newPrice }
                            }
                        });
                    }
                }
            }

            // Session summary
            if (_shopSalesCount > 0)
            {
                var profit = _shopRevenue - _shopTotalValue; // Rough estimate
                results.Add(new AnalysisResult
                {
                    ProviderId = Id,
                    Title = "Session Summary",
                    Content = $"{_shopSalesCount} sales, {_shopRevenue:N0} revenue",
                    Severity = profit > 0 ? AnalysisSeverity.Opportunity : AnalysisSeverity.Info,
                    Metadata = {
                        { "sales", _shopSalesCount },
                        { "revenue", _shopRevenue }
                    }
                });
            }

            return results;
        }

        public override string GetContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Trading Context ===");
            sb.AppendLine($"Session: {_shopSalesCount} sales, {_shopRevenue:N0} revenue");
            sb.AppendLine($"Inventory: {_shopInventory.Count} items, {_shopTotalValue:N0} value");
            sb.AppendLine();

            if (_activeOpportunities.Count > 0)
            {
                sb.AppendLine("Active Arbitrage Opportunities:");
                foreach (var opp in _activeOpportunities.OrderByDescending(o => o.PotentialProfit).Take(5))
                {
                    sb.AppendLine($"  {opp.ItemName}: {opp.SpreadPercent:N1}% spread, {opp.PotentialProfit:N0} potential");
                }
                sb.AppendLine();
            }

            if (_priceHistories.Count > 0)
            {
                sb.AppendLine("Price Trends (significant changes):");
                foreach (var history in _priceHistories.Values.Where(h => h.HasTrend).Take(5))
                {
                    var trend = history.GetTrend();
                    if (Math.Abs(trend.changePercent) >= 3)
                    {
                        sb.AppendLine($"  {history.ItemName}: {trend.changePercent:+0.0;-0.0}%");
                    }
                }
            }

            return sb.ToString();
        }

        public override string GetConfigJson()
        {
            return $"{{\"id\":\"{Id}\",\"name\":\"{Name}\",\"domain\":\"{Domain}\",\"enabled\":{Enabled.ToString().ToLower()}," +
                   $"\"enableArbitrageAlerts\":{EnableArbitrageAlerts.ToString().ToLower()}," +
                   $"\"enablePriceTracking\":{EnablePriceTracking.ToString().ToLower()}," +
                   $"\"enableShopAnalysis\":{EnableShopAnalysis.ToString().ToLower()}," +
                   $"\"arbitrageThresholdPercent\":{ArbitrageThresholdPercent:F1}," +
                   $"\"minProfitThreshold\":{MinProfitThreshold}}}";
        }

        public override void UpdateConfig(Dictionary<string, string> config)
        {
            base.UpdateConfig(config);

            if (config.TryGetValue("enableArbitrageAlerts", out var arb))
                EnableArbitrageAlerts = arb == "true" || arb == "1";
            if (config.TryGetValue("enablePriceTracking", out var track))
                EnablePriceTracking = track == "true" || track == "1";
            if (config.TryGetValue("enableShopAnalysis", out var shop))
                EnableShopAnalysis = shop == "true" || shop == "1";
            if (config.TryGetValue("arbitrageThresholdPercent", out var thresh) && double.TryParse(thresh, out var threshVal))
                ArbitrageThresholdPercent = threshVal;
            if (config.TryGetValue("minProfitThreshold", out var profit) && long.TryParse(profit, out var profitVal))
                MinProfitThreshold = profitVal;
        }

        // Track shop inventory
        public void UpdateShopInventory(Dictionary<string, ShopItem> inventory)
        {
            _shopInventory.Clear();
            foreach (var item in inventory)
            {
                _shopInventory[item.Key] = item.Value;
            }
            _shopTotalValue = _shopInventory.Values.Sum(i => i.CostBasis * i.Quantity);
        }

        // Track price update from external source
        public void UpdatePrice(string itemName, long price, string source = "external")
        {
            UpdatePriceHistory(itemName, price);

            // Check active alerts
            foreach (var alert in Context.Trading.ActiveAlerts.Values)
            {
                if (alert.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                {
                    bool triggered = false;
                    if (alert.Condition.Contains("above") && price > alert.Threshold)
                        triggered = true;
                    else if (alert.Condition.Contains("below") && price < alert.Threshold)
                        triggered = true;

                    if (triggered && !alert.Triggered)
                    {
                        alert.Triggered = true;
                        Context.PushEvent(new GameEvent
                        {
                            Type = GameEventType.PriceAlert,
                            Source = source,
                            Data = {
                                { "item", itemName },
                                { "price", price },
                                { "condition", alert.Condition }
                            }
                        });
                    }
                }
            }
        }
    }

    // Supporting classes
    public sealed class ShopItem
    {
        public string Name { get; set; }
        public int Quantity { get; set; }
        public long CostBasis { get; set; }
        public long ListPrice { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public sealed class ArbitrageOpportunity
    {
        public string ItemName { get; set; }
        public long BuyPrice { get; set; }
        public long SellPrice { get; set; }
        public double SpreadPercent { get; set; }
        public long PotentialProfit { get; set; }
        public int Volume { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class PriceHistory
    {
        public string ItemName { get; set; }
        private readonly List<(DateTime time, long price)> _prices = new List<(DateTime, long)>();
        private const int MaxHistory = 100;

        public void AddPrice(long price)
        {
            _prices.Add((DateTime.UtcNow, price));
            while (_prices.Count > MaxHistory)
                _prices.RemoveAt(0);
        }

        public bool HasTrend => _prices.Count >= 3;

        public (long oldPrice, long newPrice, double changePercent) GetTrend()
        {
            if (_prices.Count < 2) return (0, 0, 0);

            var recentPrices = _prices.Skip(Math.Max(0, _prices.Count - 5)).ToList();
            var recent = recentPrices.Average(p => p.price);
            var older = _prices.Take(Math.Max(1, _prices.Count - 5)).Average(p => p.price);

            if (older == 0) return (0, (long)recent, 0);

            var change = (recent - older) / older * 100;
            return ((long)older, (long)recent, change);
        }

        public long CurrentPrice => _prices.Count > 0 ? _prices.Last().price : 0;
        public long AveragePrice => _prices.Count > 0 ? (long)_prices.Average(p => p.price) : 0;
        public long MinPrice => _prices.Count > 0 ? _prices.Min(p => p.price) : 0;
        public long MaxPrice => _prices.Count > 0 ? _prices.Max(p => p.price) : 0;
    }
}
