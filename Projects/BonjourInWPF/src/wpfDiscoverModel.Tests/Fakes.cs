// Copyright (C) 2026 Bharath K A (bkumarbk9)
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

using System.Collections.Generic;

using Zeroconf;

namespace wpfDiscoverModel.Tests
{
    /// <summary>
    /// Hand-made Zeroconf results, so the model can be tested without a network
    /// </summary>
    internal class FakeService : IService
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public int Ttl { get; set; }
        public IReadOnlyList<IReadOnlyDictionary<string, string>> Properties { get; set; } =
            new List<IReadOnlyDictionary<string, string>>();
    }

    internal class FakeHost : IZeroconfHost
    {
        public string DisplayName { get; set; }
        public string Id { get; set; }
        public string IPAddress { get; set; }
        public IReadOnlyList<string> IPAddresses { get; set; } = new List<string>();
        public IReadOnlyDictionary<string, IService> Services { get; set; } = new Dictionary<string, IService>();

        /// <summary>
        /// A host offering one service
        /// </summary>
        public static FakeHost With(string ipAddress, string serviceType, int port, int ttl, string displayName = "Test device")
        {
            return new FakeHost
            {
                DisplayName = displayName,
                Id = ipAddress,
                IPAddress = ipAddress,
                IPAddresses = new List<string> { ipAddress },
                Services = new Dictionary<string, IService>
                {
                    { serviceType, new FakeService { Name = serviceType, Port = port, Ttl = ttl } }
                }
            };
        }
    }
}
