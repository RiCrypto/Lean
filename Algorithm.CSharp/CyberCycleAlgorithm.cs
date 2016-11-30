﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
//using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Algorithm.MyAlgorithms
{
    internal class CyberCycleAlgorithm : QCAlgorithm
    {
        DateTime startTime = DateTime.Now;
        private DateTime _startDate = new DateTime(2015, 5, 19);
        private DateTime _endDate = new DateTime(2016, 10, 1);
        private decimal _portfolioAmount = 26000;
        private decimal _transactionSize = 15000;
        private decimal lossThreshhold = 0;           // When unrealized losses fall below, revert position

        private string symbol = "BCOUSD";

        // Custom Logging
        //private ILogHandler mylog = Composer.Instance.GetExportedValueByTypeName<ILogHandler>("CustomFileLogHandler");
        //private string ondataheader = @"Time,CurrentBar,Open,High,Low,Close,Price,CyberCycle, Signal, difference, fish, stddev, negstddev, fillprice, shares owned,trade profit, profit, fees";

        private int barcount = 0;
        private RollingWindow<IndicatorDataPoint> Price;
        private CyberCycle cycle;
        private RollingWindow<IndicatorDataPoint> cycleSignal;
        private StandardDeviation standardDeviation;
        private InverseFisherTransform fish;
        private RollingWindow<IndicatorDataPoint> diff;
        private RollingWindow<IndicatorDataPoint> fishHistory;
        private RollingWindow<IndicatorDataPoint> fishDirectionHistory;
        bool fishDirectionChanged;


        private decimal factor = 1m;

        private bool openForTrading = true;
        private int sharesOwned = 0;
        private decimal portfolioProfit = 0;
        private decimal fillprice = 8;

        decimal fees = 0m;
        decimal tradeprofit = 0m;
        decimal profit = 0m;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        /// <seealso cref="QCAlgorithm.SetStartDate(System.DateTime)"/>
        /// <seealso cref="QCAlgorithm.SetEndDate(System.DateTime)"/>
        /// <seealso cref="QCAlgorithm.SetCash(decimal)"/>
        public override void Initialize()
        {
            //mylog.Debug(transheader);
            //mylog.Debug(ondataheader);

            //Initialize dates
            SetStartDate(_startDate);
            SetEndDate(_endDate);
            SetCash(22000);

            //Add as many securities as you like. All the data will be passed into the event handler:
            AddCfd(symbol, Resolution.Minute, Market.Oanda);
            SetBrokerageModel(Brokerages.BrokerageName.OandaBrokerage);
            Price = new RollingWindow<IndicatorDataPoint>(14);
            cycleSignal = new RollingWindow<IndicatorDataPoint>(14);
            cycle = new CyberCycle(7);
            Price = new RollingWindow<IndicatorDataPoint>(14);
            diff = new RollingWindow<IndicatorDataPoint>(20);
            standardDeviation = new StandardDeviation(30);
            fish = new InverseFisherTransform(10);
            fishHistory = new RollingWindow<IndicatorDataPoint>(7);
            fishDirectionHistory = new RollingWindow<IndicatorDataPoint>(7);


        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">TradeBars IDictionary object with your stock data</param>
        public void OnData(TradeBars data)
        {
            barcount++;
            var time = this.Time;
            Price.Add(idp(time, data[symbol].Close));
            cycleSignal.Add(idp(time, cycle.Current.Value));        //add last iteration value for the cycle
            cycle.Update(time, data[symbol].Close);
            diff.Add(idp(time, cycle.Current.Value - cycleSignal[0].Value));
            fish.Update(idp(time, cycle.Current.Value));
             System.Diagnostics.Debug.WriteLine(cycle.Current.Value);

            try
            {
                standardDeviation.Update(idp(time, fish.Current.Value));

            }
            catch (Exception ex)
            {

              //  throw;
            }
            fishHistory.Add(idp(time, fish.Current.Value));


            Strategy(data);

            //if (cycle.IsReady)
            //{
            //string logmsg = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}",
            //        this.Time,
            //        barcount,
            //        data[symbol].Open,
            //        data[symbol].High,
            //        data[symbol].Low,
            //        data[symbol].Close,
            //        Price[0].Value,
            //        cycle.Current.Value,
            //        cycleSignal[0].Value,
            //        diff[0].Value,
            //        fish.Current.Value,                                             //10
            //        standardDeviation.Current.Value * factor,
            //        standardDeviation.Current.Value * -factor,
            //        fillprice,
            //        sharesOwned,
            //        tradeprofit,
            //        profit,
            //        fees,
            //        "");
            //mylog.Debug(logmsg);
            //}
          //  if (barcount == 50)

              //  System.Diagnostics.Debug.WriteLine("here");
        }

        private void Strategy(TradeBars data)
        {
            if (barcount < 2)
            {
                fishDirectionHistory.Add(idp(this.Time, 0));
                fishDirectionChanged = false;
            }
            else
            {
                fishDirectionHistory.Add(idp(this.Time, Math.Sign(fishHistory[0].Value - fishHistory[1].Value)));
                fishDirectionChanged = fishDirectionHistory[0].Value != fishDirectionHistory[1].Value;
            }

            // liquidate at 3:50 to avoid the 4:00 rush.
            if (this.Time.Hour == 15 && this.Time.Minute > 49)
            {
                openForTrading = false;
                if (Portfolio.Invested)
                {
                    Liquidate();
                }
            }
            if (openForTrading)
            {
                if (cycle.IsReady)
                {
                    Invested();
                    NotInvested();
                }
            }
            //sharesOwned = Portfolio[symbol].Quantity;
        }

        private void NotInvested()
        {
            if (!Portfolio[symbol].Invested)
            {
                if (fishDirectionHistory[0].Value > 0 && fishDirectionChanged)  // if it started up
                {
                    Buy(symbol, 100);
                }
                if (fishDirectionHistory[0].Value < 0 && fishDirectionChanged) // if it started going down
                {
                    Sell(symbol, 100);
                }
            }
        }

        private void Invested()
        {
            if (Portfolio[symbol].Invested)
            {
                if (fishDirectionHistory[0].Value > 0 && fishDirectionChanged)  // if it started up
                {
                    Buy(symbol, 200);
                }
                if (fishDirectionHistory[0].Value < 0 && fishDirectionChanged) // if it started going down
                {
                    Sell(symbol, 200);
                }
                //if (Portfolio[symbol].IsShort)
                //{
                //    if (fish.Current.Value > standardDeviation.Current.Value * factor)
                //    {
                //        Buy(symbol, 200);
                //    }
                //}
                //if (Portfolio[symbol].IsLong)
                //{
                //    if (fish.Current.Value < (standardDeviation.Current.Value * -factor))
                //    {
                //        Sell(symbol, 200);
                //    }
                //}
            }
        }

        private IndicatorDataPoint idp(DateTime time, decimal value)
        {
            return new IndicatorDataPoint(time, value);
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {

            base.OnOrderEvent(orderEvent);
            if (orderEvent.Status == OrderStatus.Filled)
            {
                fillprice = orderEvent.FillPrice;
                foreach (SecurityHolding holding in Portfolio.Values)
                {
                    fees = holding.TotalFees;
                    tradeprofit = holding.LastTradeProfit;
                    profit = holding.Profit;
                }
            }
        }

        public override void OnEndOfAlgorithm()
        {
            StringBuilder sb = new StringBuilder();
            //sb.Append(" Symbols: ");
            //foreach (var s in Symbols)
            //{

            //    sb.Append(s.ToString());
            //    sb.Append(",");
            //}
            //string symbolsstring = sb.ToString();
            string symbolsstring = symbol;
            string debugstring =
                string.Format(
                    "\nAlgorithm Name: {0}\n Symbol: {1}\n Ending Portfolio Value: {2} \n lossThreshhold = {3}\n Start Time: {4}\n End Time: {5}",
                    this.GetType().Name, symbolsstring, Portfolio.TotalPortfolioValue, lossThreshhold, startTime,
                    DateTime.Now);
            //Logging.Log.Trace(debugstring);
            #region logging

            //NotifyUser();
            //using (
            //    StreamWriter sw =
            //        new StreamWriter(string.Format(@"{0}Logs\{1}.csv", AssemblyLocator.ExecutingDirectory(), symbol)))
            //{
            //    sw.Write(minuteHeader.ToString());
            //    sw.Write(minuteReturns.ToString());
            //    sw.Flush();
            //    sw.Close();
            //}

            #endregion
        }

    }
}
