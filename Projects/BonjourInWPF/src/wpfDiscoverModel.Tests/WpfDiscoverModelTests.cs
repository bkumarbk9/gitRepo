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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zeroconf;

namespace wpfDiscoverModel.Tests
{
    [TestClass]
    public class WpfDiscoverModelTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0);

        private WpfDiscoverModel model;
        private DateTime now;
        private List<string> published;
        private List<string> unpublished;

        [TestInitialize]
        public void Setup()
        {
            now = Start;
            // No background expiry thread: the tests call ProcessExpiry themselves with a fake clock
            model = new WpfDiscoverModel(startExpiryThread: false) { Clock = () => now };
            published = new List<string>();
            unpublished = new List<string>();
            model.DevicePublished += key => { lock (published) { published.Add(key); } };
            model.DeviceUnpublished += key => { lock (unpublished) { unpublished.Add(key); } };
        }

        [TestCleanup]
        public void Cleanup()
        {
            model.Dispose();
        }

        [TestMethod]
        public void AddNewDevices_PublishesService_WithCorrectFields()
        {
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_ipp._tcp.local.", 631, 120, "Office Printer"));

            Assert.AreEqual(1, published.Count);
            DiscoveredDevice device = model.GetDiscoveredDevice(published[0]);
            Assert.IsNotNull(device);
            Assert.AreEqual("Office Printer", device.Name);
            Assert.AreEqual("192.168.1.10", device.IPAddress);
            Assert.AreEqual("631", device.Port);
            Assert.AreEqual("_ipp._tcp.local.", device.ServiceType);
            StringAssert.EndsWith(device.DeviceImage, "printer.png");
            StringAssert.Contains(model.GetDeviceDetails(device.Key), "Office Printer");
        }

        [TestMethod]
        public void AddNewDevices_KeepsIpv6AddressIntact()
        {
            model.AddNewDevices(FakeHost.With("fe80::1c2b:3d4e", "_http._tcp.local.", 80, 120));

            DiscoveredDevice device = model.GetDiscoveredDevice(published[0]);
            Assert.AreEqual("fe80::1c2b:3d4e", device.IPAddress);
        }

        [TestMethod]
        public void AddNewDevices_IgnoresNullHostAndNullServices()
        {
            model.AddNewDevices(null);
            model.AddNewDevices(new FakeHost { IPAddress = "192.168.1.10", Services = null });

            Assert.AreEqual(0, published.Count);
        }

        [TestMethod]
        public void Goodbye_RemovesDevice_AndRaisesUnpublished()
        {
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 120));
            string key = published[0];

            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 0));

            CollectionAssert.AreEqual(new[] { key }, unpublished);
            Assert.IsNull(model.GetDiscoveredDevice(key));
        }

        [TestMethod]
        public void Goodbye_ForUnknownDevice_RaisesNothing()
        {
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 0));

            Assert.AreEqual(0, published.Count);
            Assert.AreEqual(0, unpublished.Count);
        }

        [TestMethod]
        public void ProcessExpiry_WithNoDevices_WaitsTheDefaultInterval()
        {
            Assert.AreEqual(9000, model.ProcessExpiry());
        }

        [TestMethod]
        public void ProcessExpiry_WaitsUntilTheNextDeviceExpires()
        {
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 5));
            now = Start.AddSeconds(2);

            Assert.AreEqual(3000, model.ProcessExpiry());
        }

        [TestMethod]
        public void ProcessExpiry_MarksDeviceExpired_AndPublishesItOnce()
        {
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 10));
            string key = published[0];
            published.Clear();
            now = Start.AddSeconds(11);

            model.ProcessExpiry();

            CollectionAssert.AreEqual(new[] { key }, published);
            StringAssert.Contains(model.GetDeviceDetails(key), "TTL Expired");
        }

        [TestMethod]
        public void ProcessExpiry_AfterExpiry_DoesNotSpin()
        {
            // Regression: an already-expired device made the wait 0 ms, so the loop spun at full CPU
            model.AddNewDevices(FakeHost.With("192.168.1.10", "_http._tcp.local.", 80, 10));
            published.Clear();
            now = Start.AddSeconds(11);
            model.ProcessExpiry();

            now = Start.AddSeconds(60);
            int nextWakeupMs = model.ProcessExpiry();

            Assert.AreEqual(9000, nextWakeupMs);
            Assert.AreEqual(1, published.Count, "An expired device must only be published once");
        }

        [TestMethod]
        public void ConcurrentGoodbyesAndExpiryChecks_DoNotThrow()
        {
            // Regression: goodbyes removed devices outside the lock while the expiry thread enumerated them
            var errors = new List<Exception>();
            var stopwatch = Stopwatch.StartNew();

            Task writer = Task.Run(() =>
            {
                try
                {
                    int i = 0;
                    while (stopwatch.ElapsedMilliseconds < 1000)
                    {
                        string ip = "10.0.0." + (i++ % 50);
                        model.AddNewDevices(FakeHost.With(ip, "_http._tcp.local.", 80, 120));
                        model.AddNewDevices(FakeHost.With(ip, "_http._tcp.local.", 80, 0));
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) { errors.Add(ex); }
                }
            });

            Task reader = Task.Run(() =>
            {
                try
                {
                    while (stopwatch.ElapsedMilliseconds < 1000)
                    {
                        model.ProcessExpiry();
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) { errors.Add(ex); }
                }
            });

            Task.WaitAll(writer, reader);
            Assert.AreEqual(0, errors.Count, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [TestMethod]
        public void FormatZeroconfHost_HandlesMissingLists()
        {
            var host = new FakeHost
            {
                DisplayName = "Bare host",
                IPAddress = "192.168.1.20",
                IPAddresses = null,
                Services = new Dictionary<string, IService>
                {
                    { "_http._tcp.local.", new FakeService { Port = 80, Ttl = 120, Properties = null } }
                }
            };

            StringAssert.Contains(WpfDiscoverModel.FormatZeroconfHost(host), "Bare host");
        }

        [TestMethod]
        public void Dispose_StopsExpiryThread_AndBlocksInit()
        {
            var threadedModel = new WpfDiscoverModel();
            var stopwatch = Stopwatch.StartNew();

            threadedModel.Dispose();
            threadedModel.Dispose(); // A second call does nothing

            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2000, "Dispose should not wait for the full expiry interval");
            Assert.ThrowsException<ObjectDisposedException>(() => threadedModel.Init());
        }
    }

    [TestClass]
    public class DiscoveredDeviceTests
    {
        [DataTestMethod]
        [DataRow("_ipp._tcp.local.", "printer.png")]
        [DataRow("_IPPS._tcp.local.", "printer.png")]
        [DataRow("_uscan._tcp.local.", "scan.png")]
        [DataRow("_nvstream_dbd._tcp.local.", "nvidia.png")]
        [DataRow("_companion-link._tcp.local.", "apple.png")]
        [DataRow("_smb._tcp.local.", "smbShare.png")]
        [DataRow("_something-else._tcp.local.", "unknown.png")]
        [DataRow(null, "unknown.png")]
        public void DeviceImage_MatchesServiceType(string serviceType, string expectedImage)
        {
            var device = new DiscoveredDevice("key", "name", "192.168.1.10", "80", serviceType);

            Assert.AreEqual(expectedImage, System.IO.Path.GetFileName(device.DeviceImage));
            StringAssert.StartsWith(device.DeviceImage, AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
