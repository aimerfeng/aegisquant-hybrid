using Xunit;
using FsCheck;
using FsCheck.Xunit;
using AegisQuant.UI.Controls;
using AegisQuant.UI.Models;
using AegisQuant.UI.Services;
using AegisQuant.UI.Strategy;
using ScottPlot;
using System.Windows;

namespace AegisQuant.UI.Tests;

/// <summary>
/// Property-based tests for CandlestickChartControl.
/// Validates Properties 13, 14 from the design document.
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.6**
/// </summary>
public class ChartPropertyTests
{
    /// <summary>
    /// Property 13: Trade Markers on Signals
    /// *For any* Buy signal, the chart SHALL display a green upward arrow marker; 
    /// for any Sell signal, a red downward arrow marker.
    /// **Validates: Requirements 6.1, 6.2, 6.3**
    /// 
    /// Note: This test validates the TradeMarkerManager behavior since the actual
    /// chart rendering requires WPF UI thread. The marker data model correctly
    /// tracks buy/sell signals with appropriate colors.
    /// </summary>
    [Fact]
    public void Property13_TradeMarkersOnSignals_BuySignal_CreatesUpwardMarker()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        int barIndex = 10;
        double price = 100.0;
        
        // Act
        manager.AddBuy(barIndex, price, 100, DateTime.Now);
        
        // Assert
        Assert.Single(manager.Markers);
        var marker = manager.Markers[0];
        Assert.True(marker.IsBuy);
        Assert.False(marker.IsSell);
        Assert.Equal(barIndex, marker.BarIndex);
        Assert.Equal(price, marker.Price);
        Assert.Equal(TradeDirection.Buy, marker.Direction);
        Assert.Equal("▲", marker.MarkerSymbol); // Upward arrow for buy
    }

    /// <summary>
    /// Property 13: Trade Markers on Signals - Sell Signal
    /// **Validates: Requirements 6.1, 6.2, 6.3**
    /// </summary>
    [Fact]
    public void Property13_TradeMarkersOnSignals_SellSignal_CreatesDownwardMarker()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        int barIndex = 15;
        double price = 95.0;
        
        // Act
        manager.AddSell(barIndex, price, 100, DateTime.Now);
        
        // Assert
        Assert.Single(manager.Markers);
        var marker = manager.Markers[0];
        Assert.False(marker.IsBuy);
        Assert.True(marker.IsSell);
        Assert.Equal(barIndex, marker.BarIndex);
        Assert.Equal(price, marker.Price);
        Assert.Equal(TradeDirection.Sell, marker.Direction);
        Assert.Equal("▼", marker.MarkerSymbol); // Downward arrow for sell
    }

    /// <summary>
    /// Property 13: Trade Markers on Signals - Property-based test
    /// *For any* signal type (Buy or Sell), the marker SHALL have the correct direction and symbol.
    /// **Validates: Requirements 6.1, 6.2, 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property13_TradeMarkersOnSignals_CorrectDirectionAndSymbol()
    {
        return Prop.ForAll(
            Arb.From<bool>(),
            Arb.From<PositiveInt>(),
            Arb.From<NormalFloat>(),
            (isBuy, barIndexGen, priceGen) =>
            {
                var manager = new TradeMarkerManager();
                int barIndex = barIndexGen.Get;
                double price = Math.Abs(priceGen.Get) + 1.0; // Ensure positive price
                
                if (isBuy)
                {
                    manager.AddBuy(barIndex, price, 100, DateTime.Now);
                }
                else
                {
                    manager.AddSell(barIndex, price, 100, DateTime.Now);
                }
                
                var marker = manager.Markers[0];
                
                // Verify direction matches
                bool directionCorrect = isBuy ? marker.IsBuy : marker.IsSell;
                
                // Verify symbol matches
                bool symbolCorrect = isBuy ? marker.MarkerSymbol == "▲" : marker.MarkerSymbol == "▼";
                
                // Verify bar index and price are stored correctly
                bool dataCorrect = marker.BarIndex == barIndex && Math.Abs(marker.Price - price) < 0.0001;
                
                return directionCorrect && symbolCorrect && dataCorrect;
            });
    }

    /// <summary>
    /// Property 13: Trade Markers on Signals - Color verification
    /// Buy markers use UpColor (red in China scheme), Sell markers use DownColor (green in China scheme).
    /// **Validates: Requirements 6.1, 6.2, 6.3**
    /// </summary>
    [Fact]
    public void Property13_TradeMarkersOnSignals_ColorsMatchColorScheme()
    {
        // Arrange
        var colorService = ColorSchemeService.Instance;
        var manager = new TradeMarkerManager();
        
        // Act
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(2, 95.0, 100, DateTime.Now);
        
        // Assert
        var buyMarker = manager.Markers[0];
        var sellMarker = manager.Markers[1];
        
        // Buy marker should use UpColor
        Assert.Equal(colorService.UpColor, buyMarker.MarkerColor);
        
        // Sell marker should use DownColor
        Assert.Equal(colorService.DownColor, sellMarker.MarkerColor);
    }

    /// <summary>
    /// Property 14: Marker Persistence
    /// *For any* trade marker added during replay, the marker SHALL remain visible until Reset() is called.
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Fact]
    public void Property14_MarkerPersistence_MarkersRemainUntilClear()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        
        // Act - Add multiple markers
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(2, 95.0, 100, DateTime.Now);
        manager.AddBuy(5, 98.0, 50, DateTime.Now);
        
        // Assert - All markers should be present
        Assert.Equal(3, manager.Count);
        Assert.Equal(2, manager.BuyMarkers.Count());
        Assert.Equal(1, manager.SellMarkers.Count());
        
        // Act - Clear markers (simulating Reset)
        manager.Clear();
        
        // Assert - All markers should be removed
        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.Markers);
    }

    /// <summary>
    /// Property 14: Marker Persistence - Property-based test
    /// *For any* sequence of N markers added, all N markers SHALL remain until Clear() is called.
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property14_MarkerPersistence_AllMarkersRemainUntilClear()
    {
        return Prop.ForAll(
            Gen.Choose(1, 50).ToArbitrary(),
            markerCount =>
            {
                var manager = new TradeMarkerManager();
                var random = new System.Random();
                
                // Add random markers
                for (int i = 0; i < markerCount; i++)
                {
                    if (random.Next(2) == 0)
                    {
                        manager.AddBuy(i, 100.0 + random.NextDouble() * 10, 100, DateTime.Now);
                    }
                    else
                    {
                        manager.AddSell(i, 100.0 + random.NextDouble() * 10, 100, DateTime.Now);
                    }
                }
                
                // Verify all markers are present
                bool allPresent = manager.Count == markerCount;
                
                // Clear and verify all removed
                manager.Clear();
                bool allCleared = manager.Count == 0;
                
                return allPresent && allCleared;
            });
    }

    /// <summary>
    /// Property 14: Marker Persistence - Markers persist across multiple operations
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Fact]
    public void Property14_MarkerPersistence_MarkersNotAffectedByOtherOperations()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        
        // Add initial markers
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(2, 95.0, 100, DateTime.Now);
        
        // Act - Perform various read operations
        var buyMarkers = manager.BuyMarkers.ToList();
        var sellMarkers = manager.SellMarkers.ToList();
        var markersAt1 = manager.GetMarkersAt(1).ToList();
        var markersInRange = manager.GetMarkersInRange(0, 5).ToList();
        
        // Assert - Original markers should still be present
        Assert.Equal(2, manager.Count);
        Assert.Single(buyMarkers);
        Assert.Single(sellMarkers);
        Assert.Single(markersAt1);
        Assert.Equal(2, markersInRange.Count);
    }

    /// <summary>
    /// Property 14: Marker Persistence - Individual marker removal
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Fact]
    public void Property14_MarkerPersistence_IndividualMarkerRemoval()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(2, 95.0, 100, DateTime.Now);
        manager.AddBuy(3, 98.0, 50, DateTime.Now);
        
        // Act - Remove one marker
        var markerToRemove = manager.Markers[1]; // The sell marker
        bool removed = manager.Remove(markerToRemove);
        
        // Assert
        Assert.True(removed);
        Assert.Equal(2, manager.Count);
        Assert.Equal(2, manager.BuyMarkers.Count());
        Assert.Empty(manager.SellMarkers);
    }

    /// <summary>
    /// Test TradeMarker factory methods
    /// </summary>
    [Fact]
    public void TradeMarker_FactoryMethods_CreateCorrectMarkers()
    {
        // Arrange & Act
        var buyMarker = TradeMarker.CreateBuy(10, 100.0, 50, DateTime.Now, "ORDER001");
        var sellMarker = TradeMarker.CreateSell(15, 95.0, 50, DateTime.Now, "ORDER002");
        
        // Assert - Buy marker
        Assert.Equal(10, buyMarker.BarIndex);
        Assert.Equal(100.0, buyMarker.Price);
        Assert.Equal(50, buyMarker.Quantity);
        Assert.Equal(TradeDirection.Buy, buyMarker.Direction);
        Assert.Equal("ORDER001", buyMarker.OrderId);
        Assert.True(buyMarker.IsBuy);
        Assert.False(buyMarker.IsSell);
        
        // Assert - Sell marker
        Assert.Equal(15, sellMarker.BarIndex);
        Assert.Equal(95.0, sellMarker.Price);
        Assert.Equal(50, sellMarker.Quantity);
        Assert.Equal(TradeDirection.Sell, sellMarker.Direction);
        Assert.Equal("ORDER002", sellMarker.OrderId);
        Assert.False(sellMarker.IsBuy);
        Assert.True(sellMarker.IsSell);
    }

    /// <summary>
    /// Test TradeMarker tooltip text generation
    /// </summary>
    [Fact]
    public void TradeMarker_TooltipText_ContainsRequiredInfo()
    {
        // Arrange
        var timestamp = new DateTime(2024, 1, 15, 10, 30, 0);
        var buyMarker = TradeMarker.CreateBuy(10, 100.50, 1000, timestamp);
        var sellMarker = TradeMarker.CreateSell(15, 95.25, 500, timestamp);
        
        // Act
        var buyTooltip = buyMarker.TooltipText;
        var sellTooltip = sellMarker.TooltipText;
        
        // Assert - Buy tooltip contains required info
        Assert.Contains("买入", buyTooltip);
        Assert.Contains("1,000", buyTooltip); // Quantity formatted
        Assert.Contains("100.50", buyTooltip); // Price
        Assert.Contains("2024-01-15", buyTooltip); // Date
        
        // Assert - Sell tooltip contains required info
        Assert.Contains("卖出", sellTooltip);
        Assert.Contains("500", sellTooltip); // Quantity
        Assert.Contains("95.25", sellTooltip); // Price
    }

    /// <summary>
    /// Test TradeMarkerManager event notification
    /// </summary>
    [Fact]
    public void TradeMarkerManager_MarkersChanged_EventFired()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        int eventCount = 0;
        manager.MarkersChanged += (s, e) => eventCount++;
        
        // Act
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(2, 95.0, 100, DateTime.Now);
        manager.Remove(manager.Markers[0]);
        manager.Clear();
        
        // Assert - Event should be fired for each operation
        Assert.Equal(4, eventCount);
    }

    /// <summary>
    /// Test GetMarkersInRange functionality
    /// </summary>
    [Fact]
    public void TradeMarkerManager_GetMarkersInRange_ReturnsCorrectMarkers()
    {
        // Arrange
        var manager = new TradeMarkerManager();
        manager.AddBuy(1, 100.0, 100, DateTime.Now);
        manager.AddSell(5, 95.0, 100, DateTime.Now);
        manager.AddBuy(10, 98.0, 50, DateTime.Now);
        manager.AddSell(15, 92.0, 75, DateTime.Now);
        manager.AddBuy(20, 105.0, 100, DateTime.Now);
        
        // Act
        var markersInRange = manager.GetMarkersInRange(5, 15).ToList();
        
        // Assert
        Assert.Equal(3, markersInRange.Count);
        Assert.Contains(markersInRange, m => m.BarIndex == 5);
        Assert.Contains(markersInRange, m => m.BarIndex == 10);
        Assert.Contains(markersInRange, m => m.BarIndex == 15);
    }
}
