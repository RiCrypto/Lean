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
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.Bitfinex.Tests
{

    [TestFixture()]
    public class BitfinexWebsocketsBrokerageTests
    {

        BitfinexWebsocketsBrokerage unit;
        Mock<IWebSocket> mock = new Mock<IWebSocket>();
        Mock<ISecurityProvider> mockSecurities;

        [SetUp()]
        public void Setup()
        {
            Config.Set("bitfinex-api-secret", "abc");
            Config.Set("bitfinex-api-key", "123");
            mockSecurities = new Mock<ISecurityProvider>();
            unit = new BitfinexWebsocketsBrokerage(mockSecurities.Object);
            //DI would be preferable here
            unit.WebSocket = mock.Object;
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

            unit.CachedOrderIDs.TryAdd(1, new BitfinexOrder { BrokerId = new List<string> { brokerId } });

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(300, e.FillQuantity);
                Assert.AreEqual(0.04, e.FillPrice);
                Assert.AreEqual(0.06, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.Filled, e.Status);
            };

            unit.OnMessage(unit, GetArgs(json));
        }

        [Test()]
        public void OnMessageTradePartialFillTest2()
        {
            string brokerId = "2";
            string json = "[0,\"te\",[\"abc123\",\"BTCUSD\",1457729043," + brokerId + ",0.00543202,420.95,\"MARKET\",null]]";

            unit.CachedOrderIDs.TryAdd(1, new BitfinexOrder { BrokerId = new List<string> { brokerId } });

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(Orders.OrderStatus.PartiallyFilled, e.Status);
            };

            unit.OnMessage(unit, GetArgs(json));
        }

        [Test()]
        public void OnMessageTradePartialFillTest()
        {
            string brokerId = "2";
            string json = "[0,\"te\", [\"abc123\",\"1\",\"BTCUSD\",\"1453989092 \",\"" + brokerId + "\",\"2\",\"4\",\"<ORD_TYPE>\",\"5\",\"0\",\"\"]]";

            unit.CachedOrderIDs.TryAdd(1, new BitfinexOrder { BrokerId = new List<string> { brokerId } });

            unit.OrderStatusChanged += (s, e) =>
            {
                Assert.AreEqual("BTCUSD", e.Symbol.Value);
                Assert.AreEqual(200, e.FillQuantity);
                Assert.AreEqual(0.04, e.FillPrice);
                Assert.AreEqual(0, e.OrderFee);
                Assert.AreEqual(Orders.OrderStatus.PartiallyFilled, e.Status);
            };

            unit.OnMessage(unit, GetArgs(json));
        }

        [Test()]
        public void OnMessageTickerTest()
        {

            string json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"0\"}";

            unit.OnMessage(unit, GetArgs(json));

            json = "[\"0\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"0.01\",\"1\",\"0.01\",\"0.01\",\"0.01\"]";

            unit.OnMessage(unit, GetArgs(json));

            var actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(0.01m, actual.Price);
        }

        [Test()]
        public void OnMessageTickerTest2()
        {

            string json = "{\"event\":\"subscribed\",\"channel\":\"ticker\",\"chanId\":\"2\"}";

            unit.OnMessage(unit, GetArgs(json));

            json = "[2,432.51,5.79789796,432.74,0.00009992,-6.41,-0.01,432.72,20067.46166511,442.79,427.26]";

            unit.OnMessage(unit, GetArgs(json));

            var actual = unit.GetNextTicks().First();
            Assert.AreEqual("BTCUSD", actual.Symbol.Value);
            Assert.AreEqual(4.3272m, actual.Price);
        }


        [Test()]
        public void OnMessageInfoRestartTest()
        {
            string json = "{\"event\":\"info\",\"code\":\"20051\"}";

            mock.Setup(m => m.Connect()).Verifiable();

            unit.OnMessage(unit, GetArgs(json));

            mock.Verify();
        }

        [Test()]
        public void OnMessageInfoResubscribeTest()
        {
            string json = "{\"event\":\"info\",\"code\":20061,\"msg\":\"Resync from the Trading Engine ended\"}";

            mock.Setup(m => m.Connect()).Verifiable();

            var brokerageMock = new Mock<BitfinexWebsocketsBrokerage>();

            brokerageMock.Setup(m => m.Unsubscribe(null, null)).Verifiable();
            brokerageMock.Setup(m => m.Subscribe(null, null)).Verifiable();
            mock.Setup(m => m.Send(It.IsAny<string>())).Verifiable();

            unit.OnMessage(brokerageMock.Object, GetArgs(json));

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
