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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.Bitfinex
{

    /// <summary>
    /// Wallet update message object
    /// </summary>
    public class WalletMessage : BaseMessage
    {

        const int _wlt_name = 0;
        const int _wlt_currency = 1;
        const int _wlt_balance = 2;
        const int _wlt_interest_unsettled = 3;

        /// <summary>
        /// Constructor for Wallet Message
        /// </summary>
        /// <param name="values"></param>
        public WalletMessage(string[] values) : base(values)
        {
            WLT_NAME = AllValues[_wlt_name];
            WLT_BALANCE = GetDecimal(_wlt_balance);
            WLT_CURRENCY = AllValues[_wlt_currency];
        }

        /// <summary>
        /// Wallet Name
        /// </summary>
 		public string WLT_NAME { get; set; }
        /// <summary>
        /// Wallet Currency
        /// </summary>
        public string WLT_CURRENCY { get; set; }
        /// <summary>
        /// Wallet Balance
        /// </summary>
        public decimal WLT_BALANCE { get; set; }
        /// <summary>
        /// Wallet Interest Unsettled
        /// </summary>
        public string WLT_INTEREST_UNSETTLED { get; set; }


    }
}
