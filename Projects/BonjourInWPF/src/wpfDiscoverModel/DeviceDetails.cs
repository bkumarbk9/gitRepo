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

namespace wpfDiscoverModel
{
    /// <summary>
    /// Stores the details of one discovered service.
    /// Not thread-safe on its own: WpfDiscoverModel only uses it while holding its lock.
    /// </summary>
    internal class DeviceDetails
    {
        #region Constants
        public const int InvalidTtl = -1;
        #endregion // Constants

        #region Public Properties
        public string Key { get; }
        public string Name { get; }
        public string IPAddress { get; }
        public string Port { get; }
        public string ServiceType { get; }
        public int TTL { get; }
        public DateTime ReceivedTimestamp { get; }
        public bool Expired { get; private set; }
        public string ServiceDetails { get; private set; }
        #endregion // Public Properties

        #region Construction
        public DeviceDetails(string key, string name, string ipAddress, string port, string serviceType,
                             int ttl, string hostDetails, DateTime receivedTimestamp)
        {
            Key = key;
            Name = name;
            IPAddress = ipAddress;
            Port = port;
            ServiceType = serviceType;
            TTL = ttl;
            ReceivedTimestamp = receivedTimestamp;
            ServiceDetails = hostDetails + "\n\nTimestamp: " + receivedTimestamp;
        }
        #endregion // Construction

        #region Public Methods
        public void SetExpired(DateTime expiredAt)
        {
            Expired = true;
            ServiceDetails += "\n\n TTL Expired: " + expiredAt;
        }
        #endregion // Public Methods
    }
}
