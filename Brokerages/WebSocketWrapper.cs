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
using System;
using System.Threading;
using WebSocketSharp;

namespace QuantConnect.Brokerages
{
    /// <summary>
    /// Wrapper for WebSocket4Net to enhance testability
    /// </summary>
    public class WebSocketWrapper : IWebSocket
    {
        private Timer timer;
        private WebSocket wrapped;
        private string _url;

        /// <summary>
        /// Wraps constructor
        /// </summary>
        /// <param name="url"></param>
        public void Initialize(string url)
        {
            if (wrapped != null)
            {
                throw new InvalidOperationException("WebSocketWrapper has already been initialized for: " + _url);
            }

            _url = url;
            wrapped = new WebSocket(url);
            timer = new Timer(_ => wrapped.Ping());
            timer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

            wrapped.OnOpen += (sender, args) => OnOpen();
            wrapped.OnMessage += (sender, args) => OnMessage(new WebSocketMessage(args.Data));
            wrapped.OnError += (sender, args) => OnError(new WebSocketError(args.Message, args.Exception));
        }

        /// <summary>
        /// Wraps send method
        /// </summary>
        /// <param name="data"></param>
        public void Send(string data)
        {
            _wrapped.Send(data);
        }

        /// <summary>
        /// Wraps Connect method
        /// </summary>
        public void Connect()
        {
            if (!IsOpen)
            {
                wrapped.Connect();
            }
        }

        /// <summary>
        /// Wraps Close method
        /// </summary>
        public void Close()
        {
            _wrapped.Close();
        }

        /// <summary>
        /// Wraps IsAlive
        /// </summary>
        public bool IsOpen
        {
            get { return wrapped.IsAlive; }
        }

        /// <summary>
        /// Wraps message event
        /// </summary>
        public event EventHandler<WebSocketMessage> Message;

        /// <summary>
        /// Wraps error event
        /// </summary>
        public event EventHandler<WebSocketError> Error;

        /// <summary>
        /// Wraps open method
        /// </summary>
        public event EventHandler Open;

        /// <summary>
        /// Event invocator for the <see cref="Message"/> event
        /// </summary>
        protected virtual void OnMessage(WebSocketMessage e)
        {
            Message?.Invoke(this, e);
        }

        /// <summary>
        /// Event invocator for the <see cref="Error"/> event
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnError(WebSocketError e)
        {
            Error?.Invoke(this, e);
        }

        /// <summary>
        /// Event invocator for the <see cref="Open"/> event
        /// </summary>
        protected virtual void OnOpen()
        {
            Open?.Invoke(this, EventArgs.Empty);
        }
    }
}
