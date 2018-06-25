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
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Securities;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace QuantConnect.Brokerages.GDAX
{
    public partial class GDAXBrokerage : BaseWebsocketsBrokerage
    {
        #region IBrokerage
        /// <summary>
        /// Checks if the websocket connection is connected or in the process of connecting
        /// </summary>
        public override bool IsConnected => WebSocket.IsOpen;

        /// <summary>
        /// Creates a new order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public override bool PlaceOrder(Order order)
        {
            LockStream();

            var req = new RestRequest("/orders", Method.POST);

            dynamic payload = new ExpandoObject();

            payload.size = Math.Abs(order.Quantity);
            payload.side = order.Direction.ToString().ToLower();
            payload.type = ConvertOrderType(order.Type);
            payload.price = (order as LimitOrder)?.LimitPrice ?? ((order as StopMarketOrder)?.StopPrice ?? 0);
            payload.product_id = ConvertSymbol(order.Symbol);

            if (_algorithm.BrokerageModel.AccountType == AccountType.Margin)
            {
                payload.overdraft_enabled = true;
            }

            var orderProperties = order.Properties as GDAXOrderProperties;
            if (orderProperties != null)
            {
                if (order.Type == OrderType.Limit)
                {
                    payload.post_only = orderProperties.PostOnly;
                }
            }

            req.AddJsonBody(payload);

            GetAuthenticationToken(req);
            var response = ExecuteRestRequest(req, GdaxEndpointType.Private);

            if (response.StatusCode == HttpStatusCode.OK && response.Content != null)
            {
                var raw = JsonConvert.DeserializeObject<Messages.Order>(response.Content);

                if (raw == null || raw.Id == null)
                {
                    var errorMessage = "Reject reason: " + raw.RejectReason;
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "GDAX Order Event") { Status = OrderStatus.Invalid, Message = errorMessage });
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, (int)response.StatusCode, errorMessage));

                    UnlockStream();
                    return true;
                }

                var brokerId = raw.Id;
                if (CachedOrderIDs.ContainsKey(order.Id))
                {
                    CachedOrderIDs[order.Id].BrokerId.Add(brokerId);
                }
                else
                {
                    order.BrokerId.Add(brokerId);
                    CachedOrderIDs.TryAdd(order.Id, order);
                }

                // Add fill splits in all cases; we'll need to handle market fills too.
                FillSplit.TryAdd(order.Id, new GDAXFill(order));

                // Generate submitted event
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "GDAX Order Event") { Status = OrderStatus.Submitted });
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Information, -1, "Order completed successfully orderid:" + order.Id));

                if (order.Type == OrderType.Market)
                {
                    Logging.Log.Trace("GDAXBrokerage.PlaceOrder(Market): Response: " + response.Content);
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, (decimal) raw.FillFees, "GDAX Order Event")
                    {
                        Status = OrderStatus.Filled,
                        FillPrice = raw.FilledSize,
                        FillQuantity = raw.ExecutedValue / raw.FilledSize
                    });
                    Orders.Order outOrder = null;
                    CachedOrderIDs.TryRemove(order.Id, out outOrder);
                }

                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Information, -1, "GDAXBrokerage.PlaceOrder: Order completed successfully orderid:" + order.Id.ToString()));
                return true;
            }

            var message = $"Order failed, Order Id: {order.Id} timestamp: {order.Time} quantity: {order.Quantity} content: {response.Content}";
            OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "GDAX Order Event") { Status = OrderStatus.Invalid });
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1, message));

            UnlockStream();
            return true;
        }

        /// <summary>
        /// This operation is not supported
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public override bool UpdateOrder(Order order)
        {
            throw new NotSupportedException("GDAXBrokerage.UpdateOrder: Order update not supported. Please cancel and re-create.");
        }

        /// <summary>
        /// Cancels an order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public override bool CancelOrder(Order order)
        {
            var success = new List<bool>();

            foreach (var id in order.BrokerId)
            {
                var req = new RestRequest("/orders/" + id, Method.DELETE);
                GetAuthenticationToken(req);
                var response = ExecuteRestRequest(req, GdaxEndpointType.Private);
                success.Add(response.StatusCode == HttpStatusCode.OK);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "GDAX Order Event") { Status = OrderStatus.Canceled });
                }
            }

            return success.All(a => a);
        }

        /// <summary>
        /// Closes the websockets connection
        /// </summary>
        public override void Disconnect()
        {
            base.Disconnect();

            WebSocket.Close();
        }

        /// <summary>
        /// Gets all orders not yet closed
        /// </summary>
        /// <returns></returns>
        public override List<Order> GetOpenOrders()
        {
            var list = new List<Order>();

            var req = new RestRequest("/orders?status=open&status=pending", Method.GET);
            GetAuthenticationToken(req);
            var response = ExecuteRestRequest(req, GdaxEndpointType.Private);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"GDAXBrokerage.GetOpenOrders: request failed: [{(int) response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            var orders = JsonConvert.DeserializeObject<Messages.Order[]>(response.Content);
            foreach (var item in orders)
            {
                Order order;
                if (item.Type == "market")
                {
                    var orders = JsonConvert.DeserializeObject<Messages.Order[]>(response.Content);
                    foreach (var item in orders)
                    {
                        Order order = null;
                        if (item.Type == "market")
                        {
                            order = new MarketOrder { Price = item.Price };
                        }
                        else if (item.Type == "limit")
                        {
                            order = new LimitOrder { LimitPrice = item.Price };
                        }
                        else if (item.Type == "stop")
                        {
                            order = new StopMarketOrder { StopPrice = item.Price };
                        }
                        else
                        {
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, (int)response.StatusCode,
                                "GDAXBrokerage.GetOpenOrders: Unsupported order type returned from brokerage: " + item.Type));
                            continue;
                        }

                        order.Quantity = item.Side == "sell" ? -item.Size : item.Size;
                        order.BrokerId = new List<string> { item.Id.ToString() };
                        order.Symbol = ConvertProductId(item.ProductId);
                        order.Time = DateTime.UtcNow;
                        order.Status = ConvertOrderStatus(item);
                        order.Price = item.Price;
                        list.Add(order);
                    }
                }
                else if (item.Type == "limit")
                {
                    order = new LimitOrder { LimitPrice = item.Price };
                }
                else if (item.Type == "stop")
                {
                    order = new StopMarketOrder { StopPrice = item.Price };
                }
                else
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, (int)response.StatusCode,
                        "GDAXBrokerage.GetOpenOrders: Unsupported order type returned from brokerage: " + item.Type));
                    continue;
                }

                order.Quantity = item.Side == "sell" ? -item.Size : item.Size;
                order.BrokerId = new List<string> { item.Id };
                order.Symbol = ConvertProductId(item.ProductId);
                order.Time = DateTime.UtcNow;
                order.Status = ConvertOrderStatus(item);
                order.Price = item.Price;
                list.Add(order);
            }

            foreach (var item in list)
            {
                if (item.Status.IsOpen())
                {
                    var cached = CachedOrderIDs.Where(c => c.Value.BrokerId.Contains(item.BrokerId.First()));
                    if (cached.Any())
                    {
                        CachedOrderIDs[cached.First().Key] = item;
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Gets all open positions
        /// </summary>
        /// <returns></returns>
        public override List<Holding> GetAccountHoldings()
        {
            var list = new List<Holding>();

            var req = new RestRequest("/orders?status=active", Method.GET);
            GetAuthenticationToken(req);
            var response = RestClient.Execute(req);
            if (response != null)
            {
                foreach (var item in JsonConvert.DeserializeObject<Messages.Order[]>(response.Content))
                {

                    decimal conversionRate;
                    if (!item.ProductId.EndsWith("USD", StringComparison.InvariantCultureIgnoreCase))
                    {

                        var baseSymbol = (item.ProductId.Substring(0, 3) + "USD").ToLower();
                        var tick = this.GetTick(Symbol.Create(baseSymbol, SecurityType.Crypto, Market.GDAX));
                        conversionRate = tick.Price;
                    }
                    else
                    {
                        var tick = this.GetTick(ConvertProductId(item.ProductId));
                        conversionRate = tick.Price;
                    }

                    list.Add(new Holding
                    {
                        Symbol = ConvertProductId(item.ProductId),
                        Quantity = item.Side == "sell" ? -item.FilledSize : item.FilledSize,
                        Type = SecurityType.Crypto,
                        CurrencySymbol = item.ProductId.Substring(0, 3).ToUpper(),
                        ConversionRate = conversionRate,
                        MarketPrice = item.Price,
                        //todo: check this
                        AveragePrice = item.FilledSize > 0 ? item.ExecutedValue / item.FilledSize : 0
                    });
                }

            }
            else
            {
                Logging.Log.Error("GDAXBrokerage.GetAccountHoldings(): Null response.");
            }
            return list;
        }

        /// <summary>
        /// Gets the total account cash balance
        /// </summary>
        /// <returns></returns>
        public override List<Cash> GetCashBalance()
        {
            var list = new List<Cash>();

            var request = new RestRequest("/accounts", Method.GET);
            GetAuthenticationToken(request);
            var response = ExecuteRestRequest(request, GdaxEndpointType.Private);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"GDAXBrokerage.GetCashBalance: request failed: [{(int)response.StatusCode}] {response.StatusDescription}, Content: {response.Content}, ErrorMessage: {response.ErrorMessage}");
            }

            foreach (var item in JsonConvert.DeserializeObject<Messages.Account[]>(response.Content))
            {
                if (item.Balance > 0)
                {
                    if (item.Currency == "USD")
                    {
                        list.Add(new Cash(item.Currency, item.Balance, 1));
                    }
                    else if (new[] {"GBP", "EUR"}.Contains(item.Currency))
                    {
                        var rate = GetConversionRate(item.Currency);
                        list.Add(new Cash(item.Currency.ToUpper(), item.Balance, rate));
                    }
                    else
                    {
                        var tick = GetTick(Symbol.Create(item.Currency + "USD", SecurityType.Crypto, Market.GDAX));

                        list.Add(new Cash(item.Currency.ToUpper(), item.Balance, tick.Price));
                    }
                }
            }

            return list;
        }
        #endregion

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            _publicEndpointRateLimiter.Dispose();
            _privateEndpointRateLimiter.Dispose();
        }
    }
}
