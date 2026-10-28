# REST API Health Monitor

**A live, browser-based dashboard that checks your websites and REST APIs every few seconds and shows uptime, latency and incidents at a glance.**

An Angular 22 single-page app that pings a configurable list of endpoints, records the result of every check and turns it into status cards with latency charts, a status timeline and incident tracking. Endpoints are defined in a JSON file, so you can change what is monitored without rebuilding.

![Angular 22](https://img.shields.io/badge/Angular-22-DD0031?logo=angular&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-6.0-3178C6?logo=typescript&logoColor=white)
![Signals](https://img.shields.io/badge/Angular-Signals-DD0031?logo=angular&logoColor=white)
![Vitest](https://img.shields.io/badge/Tested%20with-Vitest-6E9F18?logo=vitest&logoColor=white)
![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)

**Try the live demo**:
<a href="https://gray-cliff-0c3ee300f.3.azurestaticapps.net/" 
   target="_blank" 
   style="display:inline-block;padding:10px 16px;background:#0078D4;color:white;border-radius:6px;text-decoration:none;font-weight:600;">
   ▶ Try Live Demo
</a>

Hosted on **Azure Static Web Apps** (Free tier), so it loads instantly with no warm-up. The checks run in your browser; open DevTools and add `?logLevel=debug` to the URL to watch them.

![REST API Health Monitor dashboard showing four endpoint cards with status, latency charts, status timelines and a failure record with a Clear button](doc/img/screenshot.png)

---

## Table of contents
- [Features](#features)
- [Getting started](#getting-started)
- [Configuring endpoints](#configuring-endpoints)
- [The mock test endpoint](#the-mock-test-endpoint)
- [How checks work](#how-checks-work)
- [Debug logging](#debug-logging)
- [Project structure](#project-structure)
- [Limitations](#limitations)
- [License](#license)

---

## Features
- **Live status cards**: every endpoint is checked regularly (the test endpoint every 3 seconds, real sites every 60 seconds) and shown as UP, DOWN or PENDING. The **Check Health Now** button checks all of them immediately.
- **Scoreboard**: running count of online and offline endpoints.
- **Latency chart**: per-endpoint bar chart, scaled to that endpoint's slowest result.
- **Status timeline**: green/red strips, where consecutive checks with the same result merge into one block showing the check count.
- **Failure record**: each card keeps the **first** and **latest** failure time and the number of failed checks. The record is never reset automatically, even after the endpoint recovers; it stays until you click that card's **Clear** button. The banner is red while the endpoint is failing and amber once it has recovered.
- **Failed-only filter**: a checkbox to show only endpoints that are currently DOWN.
- **JSON configuration**: endpoints live in `public/endpoints.json` and are loaded at runtime.
- **Built-in test endpoint**: endpoint `1` randomly hits [httpbin.org](https://httpbin.org) success, failure and slow routes so you can see every chart state.
- **Debug logging**: levelled, tagged console logging that you can turn up at runtime without redeploying.

## Getting started

Requires **Node.js** (a version supported by Angular 22) and npm.

```bash
cd src
npm install
npm start          # dev server at http://localhost:4200
```

| Command | What it does |
|---|---|
| `npm start` | Runs the dev server with live reload |
| `npm run build` | Production build into `src/dist/rest-api-health-monitor/browser` |
| `npm test` | Runs the unit tests with Vitest |

The build output is a static site, so it can be hosted on any static web host.

## Configuring endpoints

Edit `src/public/endpoints.json`:

```json
[
  { "id": "1", "name": "Mock Test Endpoint (httpbin)", "url": "https://httpbin.org" },
  { "id": "2", "name": "JSONPlaceholder API", "url": "https://jsonplaceholder.typicode.com/posts/1" }
]
```

| Field | Description |
|---|---|
| `id` | Unique id. `"1"` is reserved for the mock test endpoint. |
| `name` | Name shown on the card. |
| `url` | URL to check. |

The file is fetched when the app loads, so after deployment you can edit it and refresh the page. No rebuild is needed.

## The mock test endpoint

Endpoint `1` ignores its configured `url`. On every check it picks one of these at random:

| URL | Result |
|---|---|
| `https://httpbin.org/status/200` | UP |
| `https://httpbin.org/status/404` | DOWN |
| `https://httpbin.org/delay/1` … `/delay/5` | UP, with 1–5 s latency |

The card shows the URL that was actually hit. This exercises the charts, timeline and incident banner without needing a real outage.

## How checks work

- Each endpoint is requested with `fetch` and timed, with a **10 s timeout**.
- The mock test endpoint is checked every **3 seconds**; real sites every **60 seconds**, so a public demo doesn't flood third-party services or hit their rate limits. Intervals are set in `endpoint-state.ts` (`MOCK_CHECK_INTERVAL_MS`, `REAL_CHECK_INTERVAL_MS`).
- Each card updates as soon as its own response arrives. If an endpoint's previous request is still running, it is skipped on that tick, so slow endpoints don't pile up requests.
- **httpbin** allows cross-origin reads, so its real HTTP status is used (2xx = UP, anything else = DOWN).
- **Other sites** usually block cross-origin reads, so they are requested in `no-cors` mode. Any response counts as UP; a network error or timeout counts as DOWN. HTTP error codes such as 404 or 500 from these sites cannot be seen. See [Limitations](#limitations).

## Debug logging

All logging goes through `Logger` (`src/src/app/core/services/logger.ts`). Messages are tagged `[HealthMonitor:<source>]`, so you can type `HealthMonitor` into the DevTools console filter to see only them.

| Level | What is logged |
|---|---|
| `error` | `endpoints.json` failed to load; unexpected errors during a check |
| `warn` | An endpoint went DOWN; first failure recorded |
| `info` | Endpoints loaded; recovery; failure record cleared by the user |
| `debug` | Every check, with URL, result reason (`HTTP 404`, `timeout`, `network error`…) and latency |

The default level is `debug` in development and `warn` in production builds. To change it:

- **For one visit:** add `?logLevel=debug` to the URL.
- **Remembered across reloads:** run `healthMonitor.setLogLevel('debug')` in the console. Levels are `debug`, `info`, `warn`, `error` and `off`.
- **Recent history:** `healthMonitor.logs()` shows the last 500 entries as a table; `healthMonitor.exportLogs()` returns them as JSON to share.

Chrome hides `debug` messages unless "Verbose" is enabled in the console's level dropdown.

## Project structure

```
rest-api-health-monitor/
├── LICENSE
├── README.md
└── src/                          Angular workspace
    ├── angular.json
    ├── package.json
    ├── public/
    │   └── endpoints.json        Endpoints to monitor
    └── src/
        ├── index.html
        ├── main.ts
        └── app/
            ├── app.ts / .html / .css     Dashboard component
            ├── app.config.ts             Loads endpoints.json at startup
            ├── models/
            │   └── endpoint.model.ts
            └── core/services/
                ├── endpoint-state.ts     Health checks, history, failure record
                └── logger.ts             Levelled console logging
```

## Limitations
- **Client-side only.** Checks run in the viewer's browser, only while the page is open. History is kept in memory and is lost on refresh.
- **No HTTP status for most sites.** Browser cross-origin rules mean only "reachable or not" can be detected for sites that don't allow cross-origin reads. Reading real status codes for every site needs a small backend or proxy.
- **Rate limits.** Every open browser tab runs its own checks. Real sites are checked once a minute, but some public APIs still limit unauthenticated use; for example, the GitHub API allows 60 requests per hour per IP address, and **Check Health Now** adds extra requests.
- **Logs stay in the browser.** Nothing is sent to a server; production error reporting would need a service such as Sentry or Azure Application Insights.

## License

Licensed under the [GNU Affero General Public License v3.0](LICENSE).
