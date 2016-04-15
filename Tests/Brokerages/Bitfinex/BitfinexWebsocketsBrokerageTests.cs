﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Brokerages.Bitfinex;
using NUnit.Framework;
using WebSocketSharp;
using Newtonsoft.Json;
using System.Reflection;
using Moq;
using QuantConnect.Configuration;
using TradingApi.Bitfinex;
using System.Threading;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.Bitfinex.Tests
{

    [TestFixture()]
    public class BitfinexWebsocketsBrokerageTests
    {

        BitfinexWebsocketsBrokerage unit;
        Mock<IWebSocket> mock = new Mock<IWebSocket>();

        [SetUp()]
        public void Setup()
        {
            unit = new BitfinexWebsocketsBrokerage("wss://localhost", mock.Object, "abc", "123", "trading", 
                new Mock<BitfinexApi>(It.IsAny<string>(), It.IsAny<string>()).Object, 100m, new Mock<ISecurityProvider>().Object);
        }

        [Test()]
        public void IsConnectedTest()
        {
            mock.Setup(w => w.IsAlive).Returns(true);
            Assert.IsTrue(unit.IsConnected);
            mock.Setup(w => w.IsAlive).Returns(false);
            Assert.IsFalse(unit.IsConnected);
        }

        [Test()]
        public void ConnectTest()
        {
            mock.Setup(m => m.Connect()).Verifiable();
            mock.Setup(m => m.OnMessage(It.IsAny<EventHandler<WebSocketSharp.MessageEventArgs>>())).Verifiable();

            unit.Connect();
            mock.Verify();
        }

        [Test()]
        public void DisconnectTest()
        {
            mock.Setup(m => m.Close()).Verifiable();
            unit.Connect();
            unit.Disconnect();
            mock.Verify();
        }

        [Test()]
        public void DisposeTest()
        {
            mock.Setup(m => m.Close()).Verifiable();
            unit.Connect();
            unit.Dispose();
            mock.Verify();
            unit.Disconnect();
        }

        [Test()]
        public void OnMessageTradeTest()
        {
            string brokerId = "2";
            string json = "[0,\"tu\", [\"abc123\",\"1\",\"BTCUSD\",\"1453989092 \",\"" + brokerId + "\",\"3\",\"4\",\"<ORD_TYPE>\",\"5\",\"6\",\"USD\"]]";
            unit.CachedOrderIDs.TryAdd(1, new Orders.MarketOrder { BrokerId = new List<string> { brokerId } });
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(300, e.FillQuantity);
                Assert.AreEqual(0.04m, e.FillPrice);
                Assert.AreEqual(0.06m, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Filled, e.Status);
                raised.Set();
            };

            unit.OnMessage(unit, GetArgs(json));
            Assert.IsTrue(raised.WaitOne(1000));

        }

        [Test()]
        public void OnMessageTradeExponentTest()
        {
            string brokerId = "2";
            string json = "[0,\"tu\", [\"abc123\",\"1\",\"BTCUSD\",\"1453989092 \",\"" + brokerId + "\",\"3\",\"4\",\"<ORD_TYPE>\",\"5\",\"0.000006\",\"USD\"]]";
            unit.CachedOrderIDs.TryAdd(1, new Orders.MarketOrder { BrokerId = new List<string> { brokerId } });
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(300, e.FillQuantity);
                Assert.AreEqual(0.04m, e.FillPrice);
                Assert.AreEqual(0.00000006m, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Filled, e.Status);
                raised.Set();
            };

            unit.OnMessage(unit, GetArgs(json));
            Assert.IsTrue(raised.WaitOne(1000));
        }

        [Test()]
        public void OnMessageTradeNegativeTest()
        {
            string brokerId = "2";
            string json = "[0,\"tu\", [\"abc123\",\"1\",\"BTCUSD\",\"1453989092 \",\"" + brokerId + "\",\"3\",\"-0.000004\",\"<ORD_TYPE>\",\"5\",\"6\",\"USD\"]]";
            unit.CachedOrderIDs.TryAdd(1, new Orders.LimitOrder { BrokerId = new List<string> { brokerId } });
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(300, e.FillQuantity);
                Assert.AreEqual(-0.00000004m, e.FillPrice);
                Assert.AreEqual(0.06m, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Filled, e.Status);
                raised.Set();
            };

            unit.OnMessage(unit, GetArgs(json));
            Assert.IsTrue(raised.WaitOne(1000));
        }

        [Test()]
        public void OnMessageTradePartialFillTest()
        {
            string brokerId = "2";
            string json = "[0,\"te\", [\"abc123\",\"1\",\"BTCUSD\",\"1453989092 \",\"" + brokerId + "\",\"2\",\"4\",\"<ORD_TYPE>\",\"5\",\"0\",\"\"]]";
            unit.CachedOrderIDs.TryAdd(1, new Orders.MarketOrder { BrokerId = new List<string> { brokerId } });
            ManualResetEvent raised = new ManualResetEvent(false);

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(200, e.FillQuantity);
                Assert.AreEqual(0.04m, e.FillPrice);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.PartiallyFilled, e.Status);
                raised.Set();
            };

            unit.OnMessage(unit, GetArgs(json));
            Assert.IsTrue(raised.WaitOne(1000));

        }

        [Test()]
        public void OnMessageTickerTest()
        {

            string json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"0\",\"pair\":\"btcusd\"}";

            unit.OnMessage(unit, GetArgs(json));

            json = "[\"0\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";

            unit.OnMessage(unit, GetArgs(json));

            var actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(0.01m, actual.Price);

            //should not serialize into exponent
            json = "[\"0\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.0000001\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";

            unit.OnMessage(unit, GetArgs(json));

            actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(0.01m, actual.Price);

            //should not fail due to parse error on superfluous field
            json = "[\"0\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"abc\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";

            unit.OnMessage(unit, GetArgs(json));

            actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(0.01m, actual.Price);

        }

        [Test()]
        public void OnMessageTickerTest2()
        {

            string json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"2\",\"pair\":\"btcusd\"}";

            unit.OnMessage(unit, GetArgs(json));

            json = "[2,432.51,5.79789796,432.74,0.00009992,-6.41,-0.01,432.72,20067.46166511,442.79,427.26]";

            unit.OnMessage(unit, GetArgs(json));

            var actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(4.3272m, actual.Price);
        }


        [Test()]
        public void OnMessageInfoHardResetTest()
        {

            string json = "{\"event\":\"info\",\"code\":\"20051\"}";

            mock.Setup(m => m.Connect()).Verifiable();
            mock.Setup(m => m.Send(It.IsAny<string>())).Verifiable();
            unit.Connect();

            unit.OnMessage(unit, GetArgs(json));

            mock.Verify();
        }

        [Test()]
        public void OnMessageInfoSoftResetTest()
        {       

            mock.Setup(m => m.Connect()).Verifiable();
            var brokerageMock = new Mock<BitfinexWebsocketsBrokerage>("wss://localhost", mock.Object, "abc", "123", "trading",
                new Mock<BitfinexApi>(It.IsAny<string>(), It.IsAny<string>()).Object, 100m, new Mock<ISecurityProvider>().Object);

            brokerageMock.Setup(m => m.Unsubscribe(null, It.IsAny<List<Symbol>>())).Verifiable();
            brokerageMock.Setup(m => m.Subscribe(null, It.IsAny<List<Symbol>>())).Verifiable();
            mock.Setup(m => m.Send(It.IsAny<string>())).Verifiable();

            //create subs
            string json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"1\",\"pair\":\"btcusd\"}";
            unit.OnMessage(unit, GetArgs(json));
            json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"2\",\"pair\":\"ethbtc\"}";
            unit.OnMessage(unit, GetArgs(json));

            //return ticks for subs.
            json = "[\"1\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";
            unit.OnMessage(brokerageMock.Object, GetArgs(json));
            json = "[\"2\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"2\",\"0.02\",\"0.02\",\"0.02\"]";
            unit.OnMessage(brokerageMock.Object, GetArgs(json));

            //ensure ticks for subs
            var actual = unit.GetNextTicks();

            var tick = actual.Where(a => a.Symbol.Value == "ETHBTC").Single();
            Assert.AreEqual(0.02m, tick.Price);

            tick = actual.Where(a => a.Symbol.Value == "BTCUSD").Single();
            Assert.AreEqual(0.01m, tick.Price);


            //trigger reset event
            json = "{\"event\":\"info\",\"code\":20061,\"msg\":\"Resync from the Trading Engine ended\"}";
            unit.OnMessage(brokerageMock.Object, GetArgs(json));

            //return new subs
            json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"2\",\"pair\":\"btcusd\"}";
            unit.OnMessage(unit, GetArgs(json));
            json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"1\",\"pair\":\"ethbtc\"}";
            unit.OnMessage(unit, GetArgs(json));

            //return ticks for new subs. eth is now 1 and btc is 2
            json = "[\"1\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";
            unit.OnMessage(brokerageMock.Object, GetArgs(json));
            json = "[\"2\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"0.02\",\"2\",\"0.02\",\"0.02\",\"0.02\"]";
            unit.OnMessage(brokerageMock.Object, GetArgs(json));

            //ensure ticks for new subs
            actual = unit.GetNextTicks();

            tick = actual.Where(a => a.Symbol.Value == "ETHBTC").Single();
            Assert.AreEqual(0.01m, tick.Price);

            tick = actual.Where(a => a.Symbol.Value == "BTCUSD").Single();
            Assert.AreEqual(0.02m, tick.Price);

            mock.Verify();
        }

        private MessageEventArgs GetArgs(string json)
        {
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            System.Globalization.CultureInfo culture = null;
            MessageEventArgs args = (MessageEventArgs)Activator.CreateInstance(typeof(MessageEventArgs), flags, null, new object[]
            {
                Opcode.Text, System.Text.Encoding.UTF8.GetBytes(json)
            }, culture);

            return args;
        }

    }
}
