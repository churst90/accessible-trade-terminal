using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IBackfillManager
    {
        void QueueBackfill(string market, string provider, string symbol, DateTime start, DateTime end);
    }

    public class BackfillManager : IBackfillManager, IDisposable
    {
        private readonly IDataService _dataService;
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IEventBus _eventBus;
        private readonly ILogger<BackfillManager> _logger;
        private readonly BlockingCollection<BackfillTask> _queue = new(boundedCapacity: 100);
        private readonly CancellationTokenSource _cts = new();

        public BackfillManager(IDataService dataService, IDbContextFactory<AppDbContext> dbContextFactory, IEventBus eventBus, ILogger<BackfillManager> logger)
        {
            _dataService = dataService;
            _dbContextFactory = dbContextFactory;
            _eventBus = eventBus;
            _logger = logger;
            SafeFireAndForget.Run(ProcessQueue, logger, "BackfillProcessQueue");
        }

        public void QueueBackfill(string market, string provider, string symbol, DateTime start, DateTime end)
        {
            _queue.Add(new BackfillTask { Market = market, Provider = provider, Symbol = symbol, Start = start, End = end });
        }

        private async Task ProcessQueue()
        {
            foreach (var task in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    _logger.LogInformation("Processing backfill for {Symbol} ({Start} to {End}).", task.Symbol, task.Start.ToString("yyyy-MM-dd"), task.End.ToString("yyyy-MM-dd"));
                    
                    // Politeness: Wait between requests to avoid rate limits
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);

                    var request = new MarketDataRequest(task.Market, task.Symbol, "1m", 1000, new DateTimeOffset(task.Start).ToUnixTimeMilliseconds());

                    var (bars, _) = await _dataService.FetchOhlcvAsync(task.Provider, request).ConfigureAwait(false);
                    
                    if (bars.Any())
                    {
                        using var context = await _dbContextFactory.CreateDbContextAsync(_cts.Token).ConfigureAwait(false);
                        
                        foreach (var bar in bars)
                        {
                            var ts = new DateTimeOffset(bar.Date).ToUnixTimeMilliseconds();
                            
                            var existing = await context.OhlcvData.FindAsync(new object[] { task.Market, task.Provider, task.Symbol, "1m", ts }, _cts.Token).ConfigureAwait(false);

                            if (existing == null)
                            {
                                context.OhlcvData.Add(new OhlcvEntity
                                {
                                    Market = task.Market,
                                    Provider = task.Provider,
                                    Symbol = task.Symbol,
                                    Timeframe = "1m",
                                    Timestamp = ts,
                                    Open = bar.Open,
                                    High = bar.High,
                                    Low = bar.Low,
                                    Close = bar.Close,
                                    Volume = bar.Volume
                                });
                            }
                            else
                            {
                                // Update if needed (though historical 1m usually doesn't change)
                                existing.Open = bar.Open;
                                existing.High = bar.High;
                                existing.Low = bar.Low;
                                existing.Close = bar.Close;
                                existing.Volume = bar.Volume;
                            }
                        }
                        await context.SaveChangesAsync(_cts.Token).ConfigureAwait(false);
                        _logger.LogInformation("Backfilled {Count} bars for {Symbol}.", bars.Count, task.Symbol);
                        _eventBus.Publish<ChartEvent>(ChartEvent.BackfillComplete(task.Symbol));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backfill task failed.");
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queue.CompleteAdding();
            _queue.Dispose();
            _cts.Dispose();
        }

        private class BackfillTask
        {
            public string Market { get; set; } = "";
            public string Provider { get; set; } = "";
            public string Symbol { get; set; } = "";
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
        }
    }
}
