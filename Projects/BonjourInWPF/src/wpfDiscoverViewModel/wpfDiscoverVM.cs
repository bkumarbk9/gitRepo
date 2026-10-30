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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;

using NLog;
using wpfDiscoverModel;

namespace wpfDiscoverViewModel
{
    /// <summary>
    /// WPF Discover View-Model class
    /// </summary>
    public class WpfDiscoverVM : INotifyPropertyChanged, IDisposable
    {
        #region Bound ICommand interfaces
        // Bound to the On/Off switch
        public ICommand OnOffCommand { get; }
        // Bound to the selected device
        public ICommand GetSelectedItemProp { get; }
        // Bound to the Apply button of the advanced options
        public ICommand ResetCommand { get; }
        #endregion // Bound ICommand interfaces

        #region Bound properties
        public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; }
        public string DeviceDetails { get; set; }
        public DiscoveredDevice SelectedItem { get; set; }
        public bool OnOffSwitch { get; set; }
        public int ScanTimeSeconds { get; set; }
        public int RetryCount { get; set; }
        public int RetryInMs { get; set; }
        public string ErrorPaneColor { get; set; }

        /// <summary>
        /// Errors and informational messages, newest last; only the most recent ones are kept
        /// </summary>
        public string Messages
        {
            get
            {
                lock (messages)
                {
                    return string.Join(Environment.NewLine, messages);
                }
            }
        }

        public bool ErrorPaneActive
        {
            private get
            {
                return errorPaneActive;
            }

            set
            {
                if (value && ErrorPaneColor == ErrorColor)
                {
                    ErrorPaneColor = ErrorSeenColor;
                    RaisePropertyChanged(nameof(ErrorPaneColor));
                }
                errorPaneActive = value;
            }
        }
        #endregion // Bound Properties

        #region Property Changed Event
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion // Property Changed Event

        #region Private Members
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly WpfDiscoverModel discoverModel;

        // Guards DiscoveredDevices (shared with WPF through EnableCollectionSynchronization) and publishedDevices
        private readonly object devicesLock = new object();
        private readonly HashSet<string> publishedDevices = new HashSet<string>();
        private readonly Queue<string> messages = new Queue<string>();
        private bool errorPaneActive;
        #endregion // Private Members

        #region Private Constants
        private const int MaxMessages = 500;
        private const string NoErrorColor = "Green";   // No error has occurred
        private const string ErrorColor = "Red";       // An error has occurred
        private const string ErrorSeenColor = "Black"; // The user has seen the error
        #endregion // Private Constants

        #region Model-View Construction
        public WpfDiscoverVM()
        {
            logger.Info("WpfDiscoverVM -- Construction");

            // Everything the model's events touch is created first: discovery starts at the end
            // and may report devices on background threads straight away
            DiscoveredDevices = new ObservableCollection<DiscoveredDevice>();
            BindingOperations.EnableCollectionSynchronization(DiscoveredDevices, devicesLock);
            ErrorPaneColor = NoErrorColor;
            OnOffSwitch = true;

            discoverModel = new WpfDiscoverModel();
            discoverModel.DevicePublished += DiscoverModel_DevicePublished;
            discoverModel.DeviceUnpublished += DiscoverModel_DeviceUnpublished;
            discoverModel.StateChanged += DiscoverModel_StateChanged;
            discoverModel.Error += DiscoverModel_Error;
            discoverModel.Message += DiscoverModel_Message;

            OnOffCommand = new GeneralRelayCmd(isOn => discoverModel.OnOff(IsTrue(isOn)));
            GetSelectedItemProp = new GeneralRelayCmd(UpdateSelectedItemDetails);
            ResetCommand = new GeneralRelayCmd(Reset);

            discoverModel.Init();

            logger.Info("WpfDiscoverVM -- Construction complete");
        }
        #endregion // Model-View Construction

        #region Public Methods
        public void Dispose()
        {
            discoverModel.Dispose();
        }
        #endregion // Public Methods

        #region Model-View bound Events
        /// <summary>
        /// Shows an error and flags the Messages pane
        /// </summary>
        /// <param name="error"></param>
        private void DiscoverModel_Error(string error)
        {
            AddMessage(error);
            if (!errorPaneActive)
            {
                ErrorPaneColor = ErrorColor;
                RaisePropertyChanged(nameof(ErrorPaneColor));
            }
            logger.Error("DiscoverModel_Error -- Error reported: [{0}]", error);
        }

        /// <summary>
        /// Shows an informational message without flagging it as an error
        /// </summary>
        /// <param name="message"></param>
        private void DiscoverModel_Message(string message)
        {
            AddMessage(message);
        }

        /// <summary>
        /// When the user or the system switches discovery on or off
        /// </summary>
        /// <param name="isOn"></param>
        private void DiscoverModel_StateChanged(bool isOn)
        {
            if (!isOn)
            {
                // Erase all devices from the view as discovery has stopped
                lock (devicesLock)
                {
                    DiscoveredDevices.Clear();
                    publishedDevices.Clear();
                }
                DeviceDetails = string.Empty;
                RaisePropertyChanged(nameof(DeviceDetails));
                logger.Info("DiscoverModel_StateChanged -- State changed to Off");
            }

            // Propagate the On/Off switch status to the view
            OnOffSwitch = isOn;
            RaisePropertyChanged(nameof(OnOffSwitch));
        }

        /// <summary>
        /// When a device is discovered or its details change
        /// </summary>
        /// <param name="deviceKey"></param>
        private void DiscoverModel_DevicePublished(string deviceKey)
        {
            DiscoveredDevice device = discoverModel.GetDiscoveredDevice(deviceKey);
            if (device == null)
            {
                return;
            }

            bool isNewDevice;
            bool isFirstDevice = false;

            lock (devicesLock)
            {
                isNewDevice = publishedDevices.Add(device.Key);
                if (isNewDevice)
                {
                    DiscoveredDevices.Add(device);
                    isFirstDevice = DiscoveredDevices.Count == 1;
                }
            }

            if (isNewDevice)
            {
                logger.Info("DiscoverModel_DevicePublished -- New device discovered: {0}, device name: {1}", device.Key, device.Name);
            }

            if (isFirstDevice)
            {
                // Select the first device so its details show straight away
                SelectedItem = device;
                RaisePropertyChanged(nameof(SelectedItem));
            }

            DiscoveredDevice selected = SelectedItem;
            if (selected != null && selected.Key == device.Key)
            {
                UpdateSelectedItemDetails(device);
            }
        }

        /// <summary>
        /// When a device announces that it is leaving the network
        /// </summary>
        /// <param name="deviceKey"></param>
        private void DiscoverModel_DeviceUnpublished(string deviceKey)
        {
            logger.Info("DiscoverModel_DeviceUnpublished -- Removing device: {0}", deviceKey);

            lock (devicesLock)
            {
                if (!publishedDevices.Remove(deviceKey))
                {
                    logger.Warn("DiscoverModel_DeviceUnpublished -- Device not found: {0}", deviceKey);
                    return;
                }

                DiscoveredDevice deviceToRemove = DiscoveredDevices.FirstOrDefault(item => item.Key == deviceKey);
                if (deviceToRemove != null)
                {
                    DiscoveredDevices.Remove(deviceToRemove);
                }
            }
        }
        #endregion // Model-View bound Events

        #region Private Methods
        /// <summary>
        /// Applies the advanced options by restarting discovery
        /// </summary>
        /// <param name="isOn">Current state of the On/Off switch</param>
        private void Reset(object isOn)
        {
            logger.Info("Reset -- Reset started");

            if (!UpdateModelConfig())
            {
                logger.Info("Reset -- No advanced options entered, nothing to apply");
                return;
            }

            discoverModel.Reset(IsTrue(isOn));

            logger.Info("Reset -- Reset complete");
        }

        private bool UpdateModelConfig()
        {
            if (ScanTimeSeconds == 0 && RetryCount == 0 && RetryInMs == 0)
            {
                return false;
            }

            discoverModel.ScanTime = TimeSpan.FromSeconds(ScanTimeSeconds);
            discoverModel.RetryCount = RetryCount;
            discoverModel.RetryDelayInMs = RetryInMs;

            return true;
        }

        private void UpdateSelectedItemDetails(object selectedDevice)
        {
            if (selectedDevice is DiscoveredDevice device)
            {
                DeviceDetails = discoverModel.GetDeviceDetails(device.Key);
                RaisePropertyChanged(nameof(DeviceDetails));
            }
        }

        private void AddMessage(string message)
        {
            lock (messages)
            {
                messages.Enqueue(message);
                while (messages.Count > MaxMessages)
                {
                    messages.Dequeue();
                }
            }
            RaisePropertyChanged(nameof(Messages));
        }

        private void RaisePropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private static bool IsTrue(object value)
        {
            return value is bool flag && flag;
        }
        #endregion // Private Methods
    }
}
