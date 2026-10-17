// Copyright (C) 2024 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Text;

using NLog;
using Zeroconf;

namespace wpfDiscoverModel
{
    /// <summary>
    /// This class stores details of discovered device
    /// </summary>
    class DeviceDetails 
    {
        #region Private Members
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private string ServiceDetails;
        private readonly WpfDiscoverModel wpfDiscModel;
        #endregion // Private Members

        #region Public Members
        public string Key { get; private set; }
        public string Domain { get; private set; }
        public string Service { get; private set; }
        public string Name { get; private set; }
        public string IP { get; private set; }
        public string Id { get; private set; }
        public int TTL { get; private set; }
        public bool Expired { get; private set; }
        public DateTime ReceivedTimestamp { get; private set; }
        public const int INVALID_TTL = -1;
        #endregion //Public Members

        #region Construction 

        public DeviceDetails(WpfDiscoverModel wpfdiscModelParam, string key, string service, string domain, 
                             string name, string ip, string id, int ttl, string serviceDetails)
        {
            wpfDiscModel = wpfdiscModelParam;
            Key = key;
            Domain = domain;
            Service = service;
            Name = name;
            IP = ip;
            Id = id;
            TTL = ttl;
            ServiceDetails = serviceDetails;
            ReceivedTimestamp = DateTime.Now;
            ServiceDetails += "\n\nTimestamp: " + ReceivedTimestamp.ToString();
        }
        #endregion // Construction

        #region Public Methods
        public string GetCompleteDeviceData()
        {
            StringBuilder deviceData = new StringBuilder();
            deviceData.AppendFormat("\nName: {0}", Name);
            deviceData.AppendFormat("\nKey: {0}", Key);
            deviceData.AppendFormat("\nDomain: {0}", Domain);
            deviceData.AppendFormat("\nService: {0}", Service);
            deviceData.AppendFormat("\n\n {0}", ServiceDetails);

            return deviceData.ToString();
        }

        public string GetDeviceDetails()
        {
            lock (wpfDiscModel)
            {
                return ServiceDetails;
            }

        }

        public void SetExpired()
        {
            Expired = true;
            ServiceDetails += "\n\n TTL Expired: " + DateTime.Now.ToString();
        }

        #endregion // Public Methods
    }
}
