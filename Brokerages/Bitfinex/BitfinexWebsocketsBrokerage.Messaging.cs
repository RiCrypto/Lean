﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Orders;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace QuantConnect.Brokerages.Bitfinex
{
    public partial class BitfinexWebsocketsBrokerage
    {

        /// <summary>
        /// Wss message handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                var raw = JsonConvert.DeserializeObject<dynamic>(e.Data);

                if (raw.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                {
                    int id = raw[0];
                    string term = raw[1];

                    if (term == "hb")
                    {
                        //heartbeat
                        _heartbeatCounter = DateTime.UtcNow;
                        return;
                    }
                    else if (term == "tu" || term == "te")
                    {
                        //trade execution/update
                        var data = raw[2].ToObject(typeof(string[]));
                        PopulateTrade(data);
                    }
                    else if (term == "ws")
                    {
                        //wallet
                        var data = raw[2].ToObject(typeof(string[][]));
                        PopulateWallet(data);
                    }
                    else if (_channelId.ContainsKey(id) && _channelId[id].Name == "ticker")
                    {
                        //ticker
                        PopulateTicker(e.Data, _channelId[id].Symbol);
                        return;
                    }
                }
                else if (raw.channel == "ticker" && raw.@event == "subscribed")
                {
                    if (!this._channelId.ContainsKey((int)raw.chanId))
                    {
                        this._channelId.Add((int)raw.chanId, new Channel { Name = "ticker", Symbol = raw.pair });
                    }
                }
                else if (raw.chanId == 0)
                {
                    if (raw.status == "FAIL")
                    {
                        throw new Exception("Failed to authenticate with ws gateway");
                    }
                    Log.Trace("Successful wss auth");
                }
                else if (raw.@event == "info" && raw.code == "20051")
                {
                    //hard reset
                    this.Reconnect();
                }
                else if (raw.@event == "info" && raw.code == "20061")
                {
                    //soft reset
                    UnAuthenticate();
                    Unsubscribe(null, null);
                    Subscribe(null, null);
                    Authenticate();
                }

                Log.Trace(e.Data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, string.Format("Parsing wss message failed. Data: {0}", e.Data));
                throw;
            }
        }
        
        private void PopulateTicker(string response, string symbol)
        {
            var data = JsonConvert.DeserializeObject<string[]>(response);
            var msg = new TickerMessage(data);
            lock (ticks)
            {
                ticks.Add(new Tick
                {
                    AskPrice = msg.ASK / scaleFactor,
                    BidPrice = msg.BID / scaleFactor,
                    AskSize = (long)Math.Round(msg.ASK_SIZE * scaleFactor, 0),
                    BidSize = (long)Math.Round(msg.BID_SIZE * scaleFactor, 0),
                    Time = DateTime.UtcNow,
                    Value = msg.LAST_PRICE / scaleFactor,
                    TickType = TickType.Quote,
                    Symbol = Symbol.Create(symbol.ToUpper(), SecurityType.Forex, Market.Bitfinex),
                    DataType = MarketDataType.Tick,
                    Quantity = (int)(Math.Round(msg.VOLUME, 2) * scaleFactor)
                });
            }
        }

        //todo: Currently populated but not used
        private void PopulateWallet(string[][] data)
        {
            if (data.Length > 0)
            {
                lock (_cash)
                {
                    _cash.Clear();
                    for (int i = 0; i < data.Length; i++)
                    {
                        var msg = new WalletMessage(data[i]);
                        _cash.Add(new Securities.Cash(msg.WLT_CURRENCY, msg.WLT_BALANCE, 1));
                    }
                }
            }
        }

        private void PopulateTrade(string[] data)
        {
            var msg = new TradeMessage(data);
            int brokerId = msg.TRD_ORD_ID;
            var cached = CachedOrderIDs.Where(o => o.Value.BrokerId.Contains(brokerId.ToString()));

            if (cached.Count() > 0 && cached.First().Value != null)
            {
                var fill = new OrderEvent
                (
                    cached.First().Key, Symbol.Create(msg.TRD_PAIR, SecurityType.Forex, Market.Bitfinex), msg.TRD_TIMESTAMP, MapOrderStatus(msg),
                    msg.TRD_AMOUNT_EXECUTED > 0 ? OrderDirection.Buy : OrderDirection.Sell,
                    msg.TRD_PRICE_EXECUTED / scaleFactor, (int)(msg.TRD_AMOUNT_EXECUTED * scaleFactor),
                    msg.FEE / scaleFactor, "Bitfinex Fill Event"
                );
                fill.FillPrice = msg.TRD_PRICE_EXECUTED / scaleFactor;

                if (msg.FEE_CURRENCY == "BTC")
                {
                    msg.FEE = (msg.FEE * msg.TRD_PRICE_EXECUTED) / scaleFactor;
                }

                filledOrderIDs.Add(cached.First().Key);

                if (fill.Status == OrderStatus.Filled)
                {
                    Order outOrder = cached.First().Value;
                    CachedOrderIDs.TryRemove(cached.First().Key, out outOrder);
                }
                OnOrderEvent(fill);
            }
            else
            {
                unknownOrderIDs.Add(brokerId);
            }
        }


        /// <summary>
        /// Authenticate with wss
        /// </summary>
        protected override void Authenticate()
        {
            string key = apiKey;
            string payload = "AUTH" + DateTime.UtcNow.Ticks.ToString();
            WebSocket.Send(JsonConvert.SerializeObject(new
            {
                @event = "auth",
                apiKey = key,
                authSig = GetHexHashSignature(payload, apiSecret),
                authPayload = payload
            }));
        }

        private void UnAuthenticate()
        {
            WebSocket.Send(JsonConvert.SerializeObject(new
            {
                @event = "unauth"
            }));
            WebSocket.Close();
        }

    }
}
