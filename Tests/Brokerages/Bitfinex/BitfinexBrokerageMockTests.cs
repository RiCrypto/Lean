﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Brokerages.Bitfinex;
using NUnit.Framework;
using Moq;
using QuantConnect.Configuration;
using TradingApi.ModelObjects.Bitfinex.Json;
using TradingApi.ModelObjects;
using QuantConnect.Securities;
using System.Threading;
using QuantConnect.Tests.Brokerages.Bitfinex;

namespace QuantConnect.Brokerages.Bitfinex.Tests
{
    [TestFixture()]
    public class BitfinexBrokerageMockTests
    {

        BitfinexBrokerage unit;
        Mock<TradingApi.Bitfinex.BitfinexApi> mock = new Mock<TradingApi.Bitfinex.BitfinexApi>(It.IsAny<string>(), It.IsAny<string>());
        Symbol symbol = Symbol.Create("BTCUSD", SecurityType.Forex, Market.Bitfinex);
        Mock<ISecurityProvider> securities = new Mock<ISecurityProvider>();
        decimal scaleFactor;

        [SetUp()]
        public void Setup()
        {
            scaleFactor = decimal.Parse(Config.Get("bitfinex-scale-factor"));
            unit = new BitfinexBrokerage("abc", "123", "trading", mock.Object, scaleFactor, securities.Object);
        }

        [Test()]
        public void PlaceOrderSubmittedTest()
        {
            var response = new BitfinexNewOrderResponse
            {
                OrderId = 1,
            };
            mock.Setup(m => m.SendOrder(It.IsAny<BitfinexNewOrderPost>())).Returns(response);

            ManualResetEvent raised = new ManualResetEvent(false);
            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Submitted, e.Status);
                raised.Set();
            };
            bool actual = unit.PlaceOrder(new Orders.MarketOrder(symbol, 100, DateTime.UtcNow));

            Assert.IsTrue(actual);
            Assert.IsTrue(raised.WaitOne(1000));
        }

        [Test()]
        public void PlaceOrderInvalidTest()
        {
            var response = new BitfinexNewOrderResponse
            {
                OrderId = 0,
            };
            mock.Setup(m => m.SendOrder(It.IsAny<BitfinexNewOrderPost>())).Returns(response);

            var raised = new ManualResetEvent(false);
            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Invalid, e.Status);
                raised.Set();
            };
            var actual = unit.PlaceOrder(new Orders.MarketOrder(symbol, 100, DateTime.UtcNow));

            Assert.IsFalse(actual);
            Assert.IsTrue(raised.WaitOne(1000));
        }

        [Test()]
        public void UpdateOrderTest()
        {
            int brokerId = 123;
            var response = new BitfinexOrderStatusResponse
            {
                Id = brokerId,
                Symbol = "BTCUSD"
            };
            mock.Setup(m => m.CancelOrder(brokerId)).Returns(response);

            var placed = new BitfinexNewOrderResponse
            {
                OrderId = 1,
                Symbol = "BTCUSD"
            };
            mock.Setup(m => m.SendOrder(It.IsAny<BitfinexNewOrderPost>())).Returns(placed);

            bool isCancel = true;
            ManualResetEvent cancel = new ManualResetEvent(false);
            ManualResetEvent open = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                if (isCancel)
                {
                    Assert.AreEqual(0, e.OrderFee);
                    Assert.AreEqual(Orders.OrderStatus.Canceled, e.Status);
                    isCancel = false;
                    cancel.Set();
                    return;
                }
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Submitted, e.Status);
                open.Set();
            };

            bool actual = unit.UpdateOrder(new Orders.MarketOrder { BrokerId = new List<string> { brokerId.ToString() }, Symbol = symbol });
            Assert.IsTrue(actual);
            Assert.IsTrue(cancel.WaitOne(1000));
            Assert.IsTrue(open.WaitOne(1000));

            //cancel fails
            response.Id = 0;
            actual = unit.UpdateOrder(new Orders.MarketOrder { BrokerId = new List<string> { "1", "2", "3" } });
            Assert.IsFalse(actual);

            //update fails
            response.Id = 2;
            placed.OrderId = 0;
            actual = unit.UpdateOrder(new Orders.MarketOrder { BrokerId = new List<string> { "1", "2", "3" } });
            Assert.IsFalse(actual);

        }

        [Test()]
        public void CancelOrderTest()
        {
            int brokerId = 123;
            var response = new BitfinexOrderStatusResponse
            {
                Id = brokerId
            };
            mock.Setup(m => m.CancelOrder(brokerId)).Returns(response);

            ManualResetEvent raised = new ManualResetEvent(false);
            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Canceled, e.Status);
                raised.Set();
            };

            bool actual = unit.CancelOrder(new Orders.MarketOrder(symbol, 100, DateTime.UtcNow) { BrokerId = new List<string> { brokerId.ToString() } });

            Assert.IsTrue(actual);

            brokerId = 0;
            actual = unit.CancelOrder(new Orders.MarketOrder(symbol, 100, DateTime.UtcNow) { BrokerId = new List<string> { brokerId.ToString() } });
            Assert.IsFalse(actual);

        }

        [Test()]
        public void GetOpenOrdersTest()
        {
            decimal expected = 456m;
            var list = new BitfinexOrderStatusResponse[] {
                new BitfinexOrderStatusResponse
                {
                    Id = 1,
                    OriginalAmount ="1",
                    Timestamp = "1",
                    Price = expected.ToString(),
                    RemainingAmount = "1",
                    ExecutedAmount = "0",
                    IsLive = true,
                    Type = "market",
                    Symbol = "ETHBTC",
                    IsCancelled = false
                }
            };
            mock.Setup(m => m.GetActiveOrders()).Returns(list);
            unit.CachedOrderIDs.TryAdd(1, new Orders.MarketOrder { BrokerId = new List<string> { "1" }, Price = 123 });

            var actual = unit.GetOpenOrders();

            Assert.AreEqual(1, actual.Count());
            Assert.AreEqual(expected / scaleFactor, unit.CachedOrderIDs.First().Value.Price);

            list.First().Id = 123;
            actual = unit.GetOpenOrders();
            Assert.AreEqual(1, actual.Count());

            list.First().Id = 1;
            list.First().ExecutedAmount = "1";
            list.First().RemainingAmount = "0";
            actual = unit.GetOpenOrders();
            Assert.AreEqual(1, actual.Count());
        }

        [Test()]
        public void GetAccountHoldingsTest()
        {

            var expected = new List<BitfinexMarginPositionResponse>
            {
                new BitfinexMarginPositionResponse
                {
                    Symbol = "btcusd",
                    Amount = "123.45",
                    Base = "456.78"
                },
                new BitfinexMarginPositionResponse
                {
                    Symbol = "ethbtc",
                    Amount = "6789.10",
                    Base = "987.65"
                }
            };

            mock.Setup(m => m.GetActivePositions()).Returns(expected);
            var btcusdTicker = new BitfinexPublicTickerGet
            {
                Mid = "654.32",
            };
            mock.Setup(m => m.GetPublicTicker("btcusd", It.IsAny<BtcInfo.BitfinexUnauthenicatedCallsEnum>())).Returns(btcusdTicker);

            var ethusdTicker = new BitfinexPublicTickerGet
            {
                Mid = "123.45",
            };
            mock.Setup(m => m.GetPublicTicker("ethusd", It.IsAny<BtcInfo.BitfinexUnauthenicatedCallsEnum>())).Returns(ethusdTicker);

            var ethbtcTicker = new BitfinexPublicTickerGet
            {
                Mid = "98.76",
            };
            mock.Setup(m => m.GetPublicTicker("ethbtc", It.IsAny<BtcInfo.BitfinexUnauthenicatedCallsEnum>())).Returns(ethbtcTicker);

            var actual = unit.GetAccountHoldings();

            Assert.AreEqual(decimal.Parse(expected.Where(e => e.Symbol == "btcusd").Single().Amount)*scaleFactor, actual.Where(e => e.Symbol.Value == "BTCUSD").Single().Quantity);
            Assert.AreEqual(decimal.Parse(btcusdTicker.Mid) / scaleFactor, actual.Where(e => e.Symbol.Value == "BTCUSD").Single().MarketPrice);
            Assert.AreEqual(decimal.Parse(btcusdTicker.Mid) / scaleFactor, actual.Where(e => e.Symbol.Value == "BTCUSD").Single().ConversionRate);

            Assert.AreEqual(decimal.Parse(expected.Where(e => e.Symbol == "ethbtc").Single().Amount)*scaleFactor, actual.Where(e => e.Symbol.Value == "ETHBTC").Single().Quantity);
            Assert.AreEqual(decimal.Parse(ethbtcTicker.Mid) / scaleFactor, actual.Where(e => e.Symbol.Value == "ETHBTC").Single().MarketPrice);
            Assert.AreEqual(decimal.Parse(ethusdTicker.Mid) / scaleFactor, actual.Where(e => e.Symbol.Value == "ETHBTC").Single().ConversionRate);

        }

        [Test()]
        public void GetCashBalanceTest()
        {

            var expected = new List<BitfinexBalanceResponse>
            {
                new BitfinexBalanceResponse
                {
                    Amount = 123.45m,
                    Currency = "usd",
                    Type = "trading"
                },
                new BitfinexBalanceResponse
                {
                    Amount = 678.90m,
                    Currency = "btc",
                    Type = "trading"

                }
            };

            var ticker = new BitfinexPublicTickerGet
            {
                Mid = "987.65"
            };
            mock.Setup(m => m.GetPublicTicker(It.IsAny<string>(), It.IsAny<BtcInfo.BitfinexUnauthenicatedCallsEnum>())).Returns(ticker);

            mock.Setup(m => m.GetBalances()).Returns(expected);

            var actual = unit.GetCashBalance();

            Assert.AreEqual(expected.Where(e => e.Currency == "usd").Single().Amount, actual.Where(e => e.Symbol == "USD").Single().Amount);
            Assert.AreEqual(1, actual.Where(e => e.Symbol == "USD").Single().ConversionRate);

            Assert.AreEqual(expected.Where(e => e.Currency == "btc").Single().Amount * scaleFactor, actual.Where(e => e.Symbol == "BTC").Single().Amount);
            Assert.AreEqual(decimal.Parse(ticker.Mid) / scaleFactor, actual.Where(e => e.Symbol == "BTC").Single().ConversionRate);

        }

        [Test()]
        public void SubscribeTest()
        {
            unit.Connect();
            var response = new BitfinexPublicTickerGet
            {
                Ask = "1",
                Bid = "2",
                High = "3",
                LastPrice = "4",
                Low = "5",
                Mid = "6",
                Timestamp = "7",
                Volume = "8"
            };
            mock.Setup(m => m.GetPublicTicker("BTCUSD", BtcInfo.BitfinexUnauthenicatedCallsEnum.pubticker)).Returns(response);
            unit.Subscribe(null, null);
            System.Threading.Thread.Sleep(9000);
            var actual = unit.GetNextTicks();
            unit.Unsubscribe(null, null);
            Assert.AreEqual(2, actual.Count());
        }

        [Test()]
        public void PlaceOrderCrossesZeroSubmittedTest()
        {
            var response = new BitfinexNewOrderResponse
            {
                OrderId = 1,
            };
            mock.Setup(m => m.SendOrder(It.IsAny<BitfinexNewOrderPost>())).Returns(response);

            var security = BitfinexTestsHelpers.GetSecurity();
            securities.Setup(s => s.GetSecurity(symbol)).Returns(security);
            securities.Object.GetSecurity(symbol).Holdings.SetHoldings(100, 100);

            int quantity = -200;
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Submitted, e.Status);
                Assert.AreEqual(0, e.FillQuantity);
                raised.Set();
            };
            bool actual = unit.PlaceOrder(new Orders.MarketOrder(symbol, quantity, DateTime.UtcNow));

            Assert.IsTrue(actual);

            Assert.IsTrue(raised.WaitOne(1000));
        }

        [Test()]
        public void PlaceOrderCrossesZeroInvalidTest()
        {
            var response = new BitfinexNewOrderResponse
            {
                OrderId = 0,
            };
            mock.Setup(m => m.SendOrder(It.IsAny<BitfinexNewOrderPost>())).Returns(response);
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Invalid, e.Status);
                raised.Set();
            };
            var actual = unit.PlaceOrder(new Orders.MarketOrder(symbol, 100, DateTime.UtcNow));
            Assert.IsFalse(actual);
            Assert.IsTrue(raised.WaitOne(1000));
        }


    }
}
