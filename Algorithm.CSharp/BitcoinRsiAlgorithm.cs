﻿using NodaTime;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.CSharp;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Custom;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.Slippage;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Algorithm.CSharp
{


    /// <summary>
    /// Sample Bitcoin Trading Algo.
    /// </summary>
    public partial class BitcoinRsiAlgorithm : BaseBitcoin
    {

        RelativeStrengthIndex rsi;
        int period = 18;

        public override void Initialize()
        {
            base.Initialize();

            SetStartDate(2016, 1, 1);
            SetEndDate(2016, 2, 1);

            rsi = RSI(BitcoinSymbol, period, MovingAverageType.Exponential, Resolution.Hour);

            var history = History<Tick>(BitcoinSymbol, this.StartDate.AddHours(-period), this.StartDate, Resolution.Tick);

            foreach (var item in history)
            {
                rsi.Update(item.Time, item.Price);
            }

        }

        private void Analyse()
        {
            if (rsi.IsReady && !this.IsWarmingUp)
            {
                Long();
                Short();
            }
        }

        public void OnData(TradeBars data)
        {
            Analyse();
        }

        public override void OnData(Tick data)
        {
            Analyse();
        }

        protected override void Long()
        {
            if (!Portfolio[BitcoinSymbol].IsLong && rsi.Current.Value > 5 && rsi.Current.Value < 30)
            {
                Liquidate();
                SetHoldings(BitcoinSymbol, 3.0m, false);
                //maker fee
                //LimitOrder(BitcoinSymbol, quantity, Portfolio[BitcoinSymbol].Price - 0.1m);
                Output("Long");
            }

        }

        private void Short()
        {
            if (!Portfolio[BitcoinSymbol].IsShort && rsi.Current.Value > 70)
            {
                Liquidate();
                SetHoldings(BitcoinSymbol, -3.0m, false);
                Output("Short");
            }
        }

    }
}