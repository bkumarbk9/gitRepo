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
using Telerik.Windows.Controls;
using NLog;

namespace wpfDiscover
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class DeviceDiscoveryMainWindow : RadWindow
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// wpfDiscover View class
        /// </summary>
        public DeviceDiscoveryMainWindow()
        {
            logger.Info("Init DeviceDiscoveryMainWindow");

            InitializeComponent();
            menuPane.ContextMenuTemplate = null;
            errorPane.ContextMenuTemplate = null;
            devicesPane.ContextMenuTemplate = null;
            detailsPane.ContextMenuTemplate = null;

            // Stop discovery and the TTL expiry thread when the window closes
            Closed += (sender, e) => (DataContext as IDisposable)?.Dispose();

            logger.Info("Init DeviceDiscoveryMainWindow complete");
        }
    }
}
