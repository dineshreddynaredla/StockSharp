namespace StockSharp.Algo.Storages.Csv
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;

	using Ecng.Collections;
	using Ecng.Common;

	using StockSharp.Messages;

	class MarketDepthCsvSerializer : CsvMarketDataSerializer<QuoteChangeMessage>
	{
		private class QuoteEnumerable : SimpleEnumerable<QuoteChangeMessage>
		{
			private class QuoteEnumerator : SimpleEnumerator<QuoteChangeMessage>
			{
				private readonly IEnumerator<TimeQuoteChange> _enumerator;
				private readonly SecurityId _securityId;

				private bool _resetCurrent = true;
				private bool _needMoveNext = true;

				public QuoteEnumerator(IEnumerator<TimeQuoteChange> enumerator, SecurityId securityId)
				{
					_enumerator = enumerator;
					_securityId = securityId;
				}

				public override bool MoveNext()
				{
					if (_resetCurrent)
					{
						Current = null;

						if (_needMoveNext && !_enumerator.MoveNext())
							return false;
					}

					_needMoveNext = true;

					Sides? side = null;

					do
					{
						var quote = _enumerator.Current;

						if (Current == null)
						{
							Current = new QuoteChangeMessage
							{
								SecurityId = _securityId,
								ServerTime = quote.ServerTime,
								LocalTime = quote.LocalTime,
								Bids = new List<QuoteChange>(),
								Asks = new List<QuoteChange>(),
								IsSorted = true,
							};
						}
						else if (Current.ServerTime != quote.ServerTime || (side == Sides.Sell && quote.Side == Sides.Buy))
						{
							_resetCurrent = true;
							_needMoveNext = false;

							return true;
						}

						side = quote.Side;

						if (quote.Price != long.MinValue && quote.Price != long.MaxValue)
						{
							var quotes = (List<QuoteChange>)(quote.Side == Sides.Buy ? Current.Bids : Current.Asks);
							quotes.Add(quote);
						}
					}
					while (_enumerator.MoveNext());

					if (Current == null)
						return false;

					_resetCurrent = true;
					_needMoveNext = true;
					return true;
				}

				public override void Reset()
				{
					_enumerator.Reset();

					_resetCurrent = true;
					_needMoveNext = true;

					base.Reset();
				}

				protected override void DisposeManaged()
				{
					_enumerator.Dispose();
					base.DisposeManaged();
				}
			}

			public QuoteEnumerable(IEnumerable<TimeQuoteChange> quotes, SecurityId securityId)
				: base(() => new QuoteEnumerator(quotes.GetEnumerator(), securityId))
			{
				if (quotes == null)
					throw new ArgumentNullException(nameof(quotes));
			}
		}

		private readonly CsvMarketDataSerializer<TimeQuoteChange> _quoteSerializer;

		public MarketDepthCsvSerializer(SecurityId securityId)
			: base(securityId)
		{
			_quoteSerializer = new QuoteCsvSerializer(securityId);
		}

		public override IMarketDataMetaInfo CreateMetaInfo(DateTime date)
		{
			return _quoteSerializer.CreateMetaInfo(date);
		}

		public override void Serialize(Stream stream, IEnumerable<QuoteChangeMessage> data, IMarketDataMetaInfo metaInfo)
		{
			var list = data.SelectMany(d =>
			{
				var items = new List<TimeQuoteChange>();

				items.AddRange(d.Bids.OrderByDescending(q => q.Price).Select(q => new TimeQuoteChange(q, d)));

				if (items.Count == 0)
				{
					items.Add(new TimeQuoteChange
					{
						Side = Sides.Buy,
						ServerTime = d.ServerTime,
						Price = long.MinValue,
					});
				}

				var bidsCount = items.Count;

				items.AddRange(d.Asks.OrderBy(q => q.Price).Select(q => new TimeQuoteChange(q, d)));

				if (items.Count == bidsCount)
				{
					items.Add(new TimeQuoteChange
					{
						Side = Sides.Sell,
						ServerTime = d.ServerTime,
						Price = long.MaxValue,
					});
				}

				return items;
			});

			_quoteSerializer.Serialize(stream, list, metaInfo);
		}

		public override IEnumerable<QuoteChangeMessage> Deserialize(Stream stream, IMarketDataMetaInfo metaInfo)
		{
			return new QuoteEnumerable(_quoteSerializer.Deserialize(stream, metaInfo), SecurityId);
		}

		protected override void Write(CsvFileWriter writer, QuoteChangeMessage data, IMarketDataMetaInfo metaInfo)
		{
			throw new NotSupportedException();
		}

		protected override QuoteChangeMessage Read(FastCsvReader reader, IMarketDataMetaInfo metaInfo)
		{
			throw new NotSupportedException();
		}
	}
}