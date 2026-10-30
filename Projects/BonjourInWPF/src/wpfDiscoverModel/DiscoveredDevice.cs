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
using System.IO;

namespace wpfDiscoverModel
{
    /// <summary>
    /// Summary of a discovered device, shown in the device list
    /// </summary>
    public class DiscoveredDevice
    {
        #region Private Constants
        // Icons are copied next to the executable, so resolve them from there rather than the
        // current directory, which differs when the app is started from a shortcut
        private static readonly string ImagesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
        private const string UnknownImage = "unknown.png";

        // Service-type prefixes and the icon shown for them, checked in order
        private static readonly (string Signature, string Image)[] ImageBySignature =
        {
            ("_printer.", "printer.png"),
            ("_pdl-datastream.", "printer.png"),
            ("_ipp.", "printer.png"),
            ("_ipps.", "printer.png"),
            ("_http.", "http.png"),
            ("_scanner.", "scan.png"),
            ("_uscans.", "scan.png"),
            ("_uscan.", "scan.png"),
            ("_nvstream_dbd.", "nvidia.png"),
            ("_companion-link.", "apple.png"),
            ("_privet.", "print-cloud.png"),
            ("_smb.", "smbShare.png"),
        };
        #endregion // Private Constants

        #region Public Properties
        public string Key { get; }
        public string Name { get; }
        public string IPAddress { get; }
        public string Port { get; }
        public string ServiceType { get; }
        public string DeviceImage { get; }
        #endregion // Public Properties

        #region Construction
        public DiscoveredDevice(string key, string name, string ipAddress, string port, string serviceType)
        {
            Key = key;
            Name = name;
            IPAddress = ipAddress;
            Port = port;
            ServiceType = serviceType;
            DeviceImage = Path.Combine(ImagesDirectory, GetImageName(serviceType));
        }
        #endregion // Construction

        #region Private Methods
        private static string GetImageName(string serviceType)
        {
            if (serviceType != null)
            {
                foreach (var entry in ImageBySignature)
                {
                    if (serviceType.StartsWith(entry.Signature, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Image;
                    }
                }
            }

            return UnknownImage;
        }
        #endregion // Private Methods
    }
}
