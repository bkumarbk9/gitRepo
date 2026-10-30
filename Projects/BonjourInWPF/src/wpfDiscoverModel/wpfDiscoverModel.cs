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
using System.Globalization;
using System.Text;
using System.Threading;

using NLog;
using Zeroconf;

namespace wpfDiscoverModel
{
    /// <summary>
    /// Model class: discovers devices with Zeroconf (mDNS/Bonjour) and tracks their TTL expiry
    /// </summary>
    public class WpfDiscoverModel : IObserver<DomainService>, IObserver<IZeroconfHost>, IDisposable
    {
        #region Private Members
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        // Guards every field below; events are always raised outside this lock
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, DeviceDetails> discoveredDevices = new Dictionary<string, DeviceDetails>();
        private readonly HashSet<string> resolvedDomains = new HashSet<string>();
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private readonly CancellationTokenSource disposeTokenSource = new CancellationTokenSource();
        private readonly Thread expiryThread;
        private bool isRunning;
        private bool isDisposed;
        #endregion // Private Members

        #region Private Constants
        private const int DefaultRetryCount = 2;
        private const int DefaultRetryDelayInMs = 2000;
        private const int DefaultWakeIntervalMs = 9000;
        private const string MessageSeparator = " | ";
        private const string KeySeparator = "@@";
        #endregion // Private Constants

        #region Model Events
        /// <summary>
        /// Raised with the device key when a device is discovered or its details change (e.g. TTL expiry)
        /// </summary>
        public event Action<string> DevicePublished;

        /// <summary>
        /// Raised with the device key when a device announces that it is leaving the network (TTL 0)
        /// </summary>
        public event Action<string> DeviceUnpublished;

        /// <summary>
        /// Raised with true when discovery starts and false when it stops
        /// </summary>
        public event Action<bool> StateChanged;

        /// <summary>
        /// Raised with a timestamped error message
        /// </summary>
        public event Action<string> Error;

        /// <summary>
        /// Raised with a timestamped informational message, such as a service announcement
        /// </summary>
        public event Action<string> Message;
        #endregion // Model Events

        #region Public Properties
        public TimeSpan ScanTime { get; set; }
        public int RetryCount { get; set; }
        public int RetryDelayInMs { get; set; }
        #endregion // Public Properties

        // Clock used for TTL bookkeeping; replaced in unit tests
        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        #region Model Construction
        public WpfDiscoverModel() : this(startExpiryThread: true)
        {
        }

        internal WpfDiscoverModel(bool startExpiryThread)
        {
            logger.Info("WpfDiscoverModel -- Model creation");
            LoadDefaultConfigs();

            if (startExpiryThread)
            {
                // Background thread, so it never keeps the process alive after the window closes
                expiryThread = new Thread(ExpiryLoop) { IsBackground = true, Name = "TTL expiry" };
                expiryThread.Start();
            }

            logger.Info("WpfDiscoverModel -- Model creation complete");
        }
        #endregion // Model Construction

        #region Public Methods
        /// <summary>
        /// Starts discovery. Does nothing if discovery is already running.
        /// </summary>
        public void Init()
        {
            lock (syncRoot)
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException(nameof(WpfDiscoverModel));
                }

                if (isRunning)
                {
                    logger.Info("Init -- Discovery is already running");
                    return;
                }

                isRunning = true;
            }

            try
            {
                AddSubscription(ZeroconfResolver.BrowseDomainsContinuous(ScanTime, RetryCount, RetryDelayInMs).Subscribe(this));
                AddSubscription(ZeroconfResolver.ListenForAnnouncementsAsync().Subscribe(OnAnnouncement));
                RaiseStateChanged(true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Init -- Failed to start discovery");
                RaiseError(ex.Message);
                Shutdown();
            }
        }

        /// <summary>
        /// Restarts discovery so new settings take effect. Does nothing while discovery is switched off.
        /// </summary>
        /// <param name="isOn">Whether discovery is currently switched on</param>
        public void Reset(bool isOn)
        {
            if (!isOn)
            {
                return;
            }

            OnOff(false);
            OnOff(true);
        }

        /// <summary>
        /// Switches discovery on or off
        /// </summary>
        /// <param name="on">True to start discovery, false to stop it</param>
        public void OnOff(bool on)
        {
            if (on)
            {
                Init();
            }
            else
            {
                Shutdown();
            }
        }

        /// <summary>
        /// Gives the summary of a discovered device, or null if the key is unknown
        /// </summary>
        /// <param name="key">Device key, as raised by DevicePublished</param>
        public DiscoveredDevice GetDiscoveredDevice(string key)
        {
            lock (syncRoot)
            {
                if (key != null && discoveredDevices.TryGetValue(key, out DeviceDetails details))
                {
                    logger.Debug("GetDiscoveredDevice -- Found: {0}", key);
                    return new DiscoveredDevice(details.Key, details.Name, details.IPAddress, details.Port, details.ServiceType);
                }
            }

            logger.Warn("GetDiscoveredDevice -- Device not found: {0}", key);
            return null;
        }

        /// <summary>
        /// Gives the detailed description of a discovered device, or an empty string if the key is unknown
        /// </summary>
        /// <param name="key">Device key, as raised by DevicePublished</param>
        public string GetDeviceDetails(string key)
        {
            lock (syncRoot)
            {
                if (key != null && discoveredDevices.TryGetValue(key, out DeviceDetails details))
                {
                    logger.Debug("GetDeviceDetails -- Found: {0}", key);
                    return details.ServiceDetails;
                }
            }

            logger.Warn("GetDeviceDetails -- Device not found: {0}", key);
            return string.Empty;
        }

        /// <summary>
        /// Loads the default discovery settings
        /// </summary>
        public void LoadDefaultConfigs()
        {
            ScanTime = default(TimeSpan);
            RetryCount = DefaultRetryCount;
            RetryDelayInMs = DefaultRetryDelayInMs;
        }

        /// <summary>
        /// Stops discovery and the TTL expiry thread
        /// </summary>
        public void Dispose()
        {
            lock (syncRoot)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
            }

            Shutdown();
            disposeTokenSource.Cancel();
            expiryThread?.Join(TimeSpan.FromSeconds(2));
        }
        #endregion // Public Methods

        #region IObserver<DomainService> Implementation
        /// <summary>
        /// Called by the Zeroconf library for each domain found while browsing
        /// </summary>
        /// <param name="value"></param>
        public void OnNext(DomainService value)
        {
            if (value == null || value.Domain == null)
            {
                return;
            }

            lock (syncRoot)
            {
                // The continuous browse reports the same domains on every scan; resolve each domain only once
                if (!isRunning || !resolvedDomains.Add(value.Domain))
                {
                    return;
                }
            }

            try
            {
                logger.Info("OnNext -- New domain: {0}, service: {1}", value.Domain, value.Service);
                AddSubscription(ZeroconfResolver.ResolveContinuous(value.Domain, ScanTime, RetryCount, RetryDelayInMs).Subscribe(this));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OnNext -- Failed to resolve domain {0}", value.Domain);
                RaiseError(ex.Message);
                Shutdown();
            }
        }

        /// <summary>
        /// Called by the Zeroconf library when browsing or resolving completes
        /// </summary>
        public void OnCompleted()
        {
            logger.Info("OnCompleted -- Completed browsing");
            RaiseMessage("Completed browsing");
        }

        /// <summary>
        /// Called by the Zeroconf library when an error occurs
        /// </summary>
        /// <param name="error"></param>
        public void OnError(Exception error)
        {
            logger.Error(error, "OnError -- Zeroconf reported an error");
            RaiseError(error?.Message ?? "Unknown Zeroconf error");
        }
        #endregion // IObserver<DomainService> Implementation

        #region IObserver<IZeroconfHost> Implementation
        /// <summary>
        /// Called by the Zeroconf library with resolved host details
        /// </summary>
        /// <param name="value"></param>
        public void OnNext(IZeroconfHost value)
        {
            AddNewDevices(value);
        }
        #endregion // IObserver<IZeroconfHost> Implementation

        #region Internal Methods
        /// <summary>
        /// Adds or refreshes the services of a resolved host; services with TTL 0 are removed
        /// </summary>
        /// <param name="zconfHost"></param>
        internal void AddNewDevices(IZeroconfHost zconfHost)
        {
            if (zconfHost == null || zconfHost.IPAddress == null || zconfHost.Services == null)
            {
                logger.Warn("AddNewDevices -- Received null host details, so disregarding");
                return;
            }

            var devicesToPublish = new List<string>();
            var devicesToUnpublish = new List<string>();
            string hostDetails = FormatZeroconfHost(zconfHost);
            DateTime now = Clock();

            lock (syncRoot)
            {
                foreach (var service in zconfHost.Services)
                {
                    if (service.Key == null || service.Value == null)
                    {
                        logger.Warn("AddNewDevices -- Received a null service, so disregarding");
                        continue;
                    }

                    string port = service.Value.Port.ToString(CultureInfo.InvariantCulture);
                    string key = zconfHost.IPAddress + KeySeparator + service.Key + KeySeparator + port;

                    if (service.Value.Ttl <= 0)
                    {
                        // TTL 0 is a "goodbye": the service is leaving the network
                        if (discoveredDevices.Remove(key))
                        {
                            devicesToUnpublish.Add(key);
                        }
                        else
                        {
                            logger.Warn("AddNewDevices -- Goodbye received for unknown device: {0}", key);
                        }
                    }
                    else
                    {
                        discoveredDevices[key] = new DeviceDetails(key, zconfHost.DisplayName, zconfHost.IPAddress, port,
                                                                   service.Key, service.Value.Ttl, hostDetails, now);
                        devicesToPublish.Add(key);
                    }
                }
            }

            // Raised outside the lock so subscribers can safely call back into the model
            foreach (string key in devicesToPublish)
            {
                RaiseDevicePublished(key);
            }

            foreach (string key in devicesToUnpublish)
            {
                RaiseDeviceUnpublished(key);
            }
        }

        /// <summary>
        /// Marks devices whose TTL has passed as expired and publishes them again so their details refresh
        /// </summary>
        /// <returns>Milliseconds to wait before the next check</returns>
        internal int ProcessExpiry()
        {
            DateTime now = Clock();
            double nextWakeupMs = DefaultWakeIntervalMs;
            var expiredDevices = new List<string>();

            lock (syncRoot)
            {
                foreach (DeviceDetails device in discoveredDevices.Values)
                {
                    // Devices that have already expired are skipped; counting them made the wait
                    // negative and the loop spin at full CPU
                    if (device.TTL == DeviceDetails.InvalidTtl || device.Expired)
                    {
                        continue;
                    }

                    double secondsLeft = device.TTL - (now - device.ReceivedTimestamp).TotalSeconds;
                    if (secondsLeft <= 0)
                    {
                        device.SetExpired(now);
                        expiredDevices.Add(device.Key);
                        logger.Info("ProcessExpiry -- Device expired: {0}", device.Key);
                    }
                    else
                    {
                        nextWakeupMs = Math.Min(nextWakeupMs, Math.Ceiling(secondsLeft * 1000));
                    }
                }
            }

            foreach (string key in expiredDevices)
            {
                RaiseDevicePublished(key);
            }

            return (int)nextWakeupMs;
        }

        /// <summary>
        /// Formats host details for display
        /// </summary>
        /// <param name="zconfHost"></param>
        internal static string FormatZeroconfHost(IZeroconfHost zconfHost)
        {
            if (zconfHost == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendFormat("Display Name: {0}", zconfHost.DisplayName);
            sb.AppendFormat("\nId: {0}", zconfHost.Id);
            sb.AppendFormat("\nIP Address: {0}", zconfHost.IPAddress);

            sb.Append("\nIP Addresses:");
            foreach (string ipAddress in zconfHost.IPAddresses ?? new string[0])
            {
                sb.AppendFormat(" {0} ", ipAddress);
            }

            sb.Append("\n\nServices:");
            foreach (var service in zconfHost.Services ?? new Dictionary<string, IService>())
            {
                if (service.Value == null)
                {
                    continue;
                }

                sb.Append("\n\n ----- Begin -----");
                sb.AppendFormat("\n Service Key: {0}", service.Key);
                sb.AppendFormat("\n Service Name: {0}", service.Value.Name);
                sb.AppendFormat("\n Service Port: {0}", service.Value.Port);
                sb.AppendFormat("\n Service TTL: {0}", service.Value.Ttl);
                sb.Append("\n Service Properties:");

                foreach (var servicePropertyList in service.Value.Properties ?? new IReadOnlyDictionary<string, string>[0])
                {
                    foreach (var serviceProperty in servicePropertyList)
                    {
                        sb.AppendFormat("\n   {0}: {1}", serviceProperty.Key, serviceProperty.Value);
                    }
                }

                sb.Append("\n ----- End -----");
            }

            return sb.ToString();
        }
        #endregion // Internal Methods

        #region Private Methods
        /// <summary>
        /// Checks TTL expiry until the model is disposed
        /// </summary>
        private void ExpiryLoop()
        {
            while (!disposeTokenSource.IsCancellationRequested)
            {
                int nextWakeupMs = DefaultWakeIntervalMs;
                try
                {
                    nextWakeupMs = ProcessExpiry();
                }
                catch (Exception ex)
                {
                    // An unhandled exception here would end the whole process
                    logger.Error(ex, "ExpiryLoop -- Unexpected error while checking TTL expiry");
                }

                disposeTokenSource.Token.WaitHandle.WaitOne(Math.Max(nextWakeupMs, 0));
            }
        }

        /// <summary>
        /// Keeps a subscription so it can be disposed on shutdown; disposes it at once if discovery has stopped
        /// </summary>
        /// <param name="subscription"></param>
        private void AddSubscription(IDisposable subscription)
        {
            lock (syncRoot)
            {
                if (isRunning)
                {
                    subscriptions.Add(subscription);
                    return;
                }
            }

            DisposeQuietly(subscription);
        }

        /// <summary>
        /// Stops discovery and forgets all discovered devices
        /// </summary>
        private void Shutdown()
        {
            List<IDisposable> toDispose;

            lock (syncRoot)
            {
                toDispose = new List<IDisposable>(subscriptions);
                toDispose.Reverse();
                subscriptions.Clear();
                resolvedDomains.Clear();
                discoveredDevices.Clear();
                isRunning = false;
            }

            foreach (IDisposable subscription in toDispose)
            {
                DisposeQuietly(subscription);
            }

            RaiseStateChanged(false);
        }

        private static void DisposeQuietly(IDisposable subscription)
        {
            try
            {
                subscription?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisposeQuietly -- Failed to dispose a subscription");
            }
        }

        /// <summary>
        /// Formats a service announcement for display
        /// </summary>
        /// <param name="sa"></param>
        private static string FormatServiceAnnouncement(ServiceAnnouncement sa)
        {
            var svcAnnouncement = new StringBuilder();
            svcAnnouncement.AppendFormat("Name: {0}", sa.AdapterInformation.Name);
            svcAnnouncement.AppendFormat("\n Address: {0}", sa.AdapterInformation.Address);
            svcAnnouncement.AppendFormat("\n {0}", FormatZeroconfHost(sa.Host));

            return svcAnnouncement.ToString();
        }

        /// <summary>
        /// Service announcement callback
        /// </summary>
        /// <param name="sa"></param>
        private void OnAnnouncement(ServiceAnnouncement sa)
        {
            if (sa == null)
            {
                return;
            }

            string svcAnnounceDetails = FormatServiceAnnouncement(sa);
            logger.Info("OnAnnouncement -- {0}", svcAnnounceDetails);
            RaiseMessage(svcAnnounceDetails);
        }

        private void RaiseStateChanged(bool state)
        {
            StateChanged?.Invoke(state);
        }

        private void RaiseError(string error)
        {
            Error?.Invoke(Clock() + MessageSeparator + error);
        }

        private void RaiseMessage(string message)
        {
            Message?.Invoke(Clock() + MessageSeparator + message);
        }

        private void RaiseDevicePublished(string key)
        {
            DevicePublished?.Invoke(key);
        }

        private void RaiseDeviceUnpublished(string key)
        {
            DeviceUnpublished?.Invoke(key);
        }
        #endregion // Private Methods
    }
}
