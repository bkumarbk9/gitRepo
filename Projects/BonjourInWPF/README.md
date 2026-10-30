# Bonjour In WPF Application
A WPF mDNS (Bonjour) discovery tool to discover available network services
# Introduction

In past 20 years, computers gradually transitioned away from platform-specific protocols such as AppleTalk, IPX, and NetBIOS towards the Internet Protocol (IP). The majority of computers and other network devices all use TCP/IP for communication. In that transition, however, one piece of functionality was lost—the ability to add devices to a local network and then connect to those devices from computers and other devices on the network, all with little or no configuration.

Consider printing using a standard printer. The moment a printer is configured in a user computer or device, it’s simply a matter of choosing an application’s Print command.  For example, take your laptop to a client’s company, or a neighbor’s house, and try to print something.  Your life will be super easy If they have a printer that supports Bonjour protocols.  If Bonjour is supported, printing is just as easy as it was on your home network. To print, connect your laptop to your client’s Wi-Fi access point and start up your laptop.  Then, your laptop automatically discovers any available printers. You open the document, click Print menu option.  All available printer appears in the Print dialog. Select a printer, click Print, and the document prints.  No configuration, search, find or any other complicated IT setup is required
![Application Image](https://github.com/bkumarbk9/BonjourInWPF/blob/main/doc/img/Screenshot.jpg?raw=true)
## Background

Bonjour is Apple’s concept for zero-configuration networking over IP. Bonjour comes out of the work of the ZEROCONF Working Group, part of the Internet Engineering Task Force (IETF). The ZEROCONF Working Group’s requirements and proposed solutions for zero-configuration networking over IP essentially cover three areas:

addressing (allocating IP addresses to hosts)

naming (using names to refer to hosts instead of IP addresses)

service discovery (finding services on the network automatically)

Bonjour has a zero-configuration solution for all three of these areas, as described in the following four sections.

Bonjour allows service providers, hardware manufacturers, and application programmers to support a single network protocol—IP—while breaking new ground in ease of use.

Network users no longer have to assign IP addresses, assign host names, or even type in names to access services on the network. Users simply ask to see what network services are available, and choose from the list.

In many ways, this kind of browsing is even more powerful for applications than for users. Applications can automatically detect services they need or other applications they can interact with, allowing automatic connection, communication, and data exchange, without requiring user intervention!

## Using the Application
1. Open Quick demo application
2. Application detects all Bonjour compliant devices, services in your local network
3. Select a device to see its details; the **Messages** tab shows errors and service announcements

> The ready-to-run files in `DemoBinaries` were built before the V1.1 fixes below. Build from source for the latest version.

## Building from source

**Prerequisites**
- Visual Studio 2022 or later with the **.NET desktop development** workload (.NET Framework 4.8)
- **Telerik UI for WPF R1 2020**, installed in its default folder. For another location or version, set `TelerikDir`, for example `msbuild /p:TelerikDir="D:\Telerik\UI for WPF\"` or a `TelerikDir` environment variable.

**Build and test**
1. Open `src/wpfDiscover.sln`. NuGet packages are restored automatically (from the command line: `msbuild src/wpfDiscover.sln -t:restore -p:RestorePackagesConfig=true`).
2. Build and run `wpfDiscover.View`.
3. Run the unit tests in **Test Explorer**, or with `dotnet test src/wpfDiscoverModel.Tests`. They cover discovery bookkeeping, TTL expiry and thread safety without needing a network.

**Project layout**

| Project | Role |
|---|---|
| `wpfDiscover.View` | WPF window (Telerik controls) |
| `wpfDiscover.ViewModel` | Bindable state and commands |
| `wpfDiscoverModel` | mDNS discovery with Zeroconf, device list and TTL expiry |
| `wpfDiscoverModel.Tests` | MSTest unit tests for the model |

## Why WPF Application

When it comes to developing high-performance desktop applications, the choice between Windows Presentation Foundation (WPF) and Windows Forms (WinForms) is pivotal. WPF’s modern architecture, leveraging hardware acceleration and vector-based rendering, often results in smoother animations and graphics. Its resolution independence also shines on high DPI displays and touch which is the norm nowadays.

WinForms, in contrast, is often acknowledged as a lightweight and high-performance UI framework. Although it may not deliver the visual sophistication of WPF, Winforms excels in offering responsive user interfaces, even on less powerful hardware.

Both frameworks are performant, but WPF excels in achieving seamless animations and enhanced graphic quality. 

## Why Telerik WPF UI

 - Build beautiful and high-performance WPF business applications with the Telerik UI for WPF components
 -  DPI independent and provide native 3D support
 - Delivers 160+ controls to meet your app requirements for data handling, performance, UX, design, accessibility, and so much more
 - Modern professional themes at finger tip
 - Exceptional performance and user experience
 - Intuitive API
 - MVVM support
 - Touch support
 - Support for .Net core

## Points to remember

This project is done as a prototype or sample.  So code brevity is traded for writing full fledged professional code.  This allows anyone to learn the pattern, concept with ease instead of spending time to skip long error/exception handling, secure programming etc,  

## License
Copyright (C) 2024 Bharath K A (bkumarbk9)

Licensed under the GNU Affero General Public License v3.0 only (`AGPL-3.0-only`).
See [LICENSE](LICENSE) for the full license text.

Third-party components (Telerik UI for WPF, NLog, Zeroconf, System.Reactive and the other libraries in `DemoBinaries` and the restored NuGet packages) remain under their own licenses.

## History
 - V1.0 -- 5th May 2024 - First version
 - V1.1 -- 29th October 2026 - Reliability fixes: no more 100% CPU after a device's TTL expires, thread-safe device list (no crash on "goodbye" messages), each network domain resolved once, IPv6 addresses shown in full, scan time in seconds, Messages pane capped at the latest 500 entries, discovery stopped cleanly on exit. Added unit tests; NuGet packages are restored instead of committed.
## References
1. https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/NetServices/Articles/about.html
2. https://github.com/novotnyllc/Zeroconf 

