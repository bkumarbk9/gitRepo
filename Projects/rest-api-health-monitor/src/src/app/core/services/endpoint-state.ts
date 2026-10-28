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

import { Injectable, signal, computed, inject } from '@angular/core';
import { ApiEndpoint, EndpointConfig } from '../../models/endpoint.model';
import { Logger } from './logger';

const LOG_SOURCE = 'EndpointState';

// Id 1 is reserved as a test endpoint that hits httpbin.org to exercise the charts
export const MOCK_ENDPOINT_ID = '1';
const MOCK_BASE_URL = 'https://httpbin.org';
const ENDPOINTS_CONFIG_URL = 'endpoints.json';
const REQUEST_TIMEOUT_MS = 10000;
// The mock endpoint is checked often to keep the charts moving; real sites are checked
// gently so a public demo doesn't flood third-party services or hit their rate limits
const MOCK_CHECK_INTERVAL_MS = 3000;
const REAL_CHECK_INTERVAL_MS = 60000;
// Timer ticks can fire a few ms early; without slack the mock would skip every other tick
const INTERVAL_SLACK_MS = 500;
const MAX_HISTORY_BLOCKS = 12;

interface PingOutcome {
  status: 'UP' | 'DOWN';
  latency: number;
  detail: string; // HTTP status, 'opaque', 'timeout' or the network error, for logging
}

@Injectable({
  providedIn: 'root'
})
export class EndpointState {
  // Populated from public/endpoints.json by loadEndpoints() at startup
  private endpointsSignal = signal<ApiEndpoint[]>([]);

  // Endpoints with a request still running, so slow checks don't pile up on each tick
  private inFlight = new Set<string>();
  private lastCheckStartedAt = new Map<string, number>();
  private logger = inject(Logger);

  // We expose this as a read-only value so components can look at it but not break it
  public endpoints = this.endpointsSignal.asReadonly();
    // Automatically counts how many APIs have an 'UP' status
  public totalUp = computed(() => {
    return this.endpoints().filter(item => item.status === 'UP').length;
  });

  // Automatically counts how many APIs have a 'DOWN' status
  public totalDown = computed(() => {
    return this.endpoints().filter(item => item.status === 'DOWN').length;
  });

  constructor() {}

  public async loadEndpoints(): Promise<void> {
    try {
      const response = await fetch(ENDPOINTS_CONFIG_URL, { cache: 'no-store' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const configs: EndpointConfig[] = await response.json();
      this.logger.info(LOG_SOURCE, `Loaded ${configs.length} endpoints from ${ENDPOINTS_CONFIG_URL}`, configs);

      this.endpointsSignal.set(configs.map(config => ({
        id: String(config.id),
        name: config.name,
        url: config.url,
        status: 'PENDING',
        responseTime: 0,
        history: [],
        firstFailedAt: null,
        lastFailedAt: null,
        failureCount: 0
      })));
    } catch (error) {
      // Leave the list empty so the page shows "No endpoints currently being monitored."
      this.logger.error(LOG_SOURCE, `Failed to load ${ENDPOINTS_CONFIG_URL}`, error);
    }
  }

  // Called on every timer tick; each endpoint is only checked once its own interval has
  // passed. force = true (the "Check Health Now" button) checks everything immediately.
  public checkHealth(force = false): void {
    const now = Date.now();
    for (const endpoint of this.endpointsSignal()) {
      const isMock = endpoint.id === MOCK_ENDPOINT_ID;
      const interval = isMock ? MOCK_CHECK_INTERVAL_MS : REAL_CHECK_INTERVAL_MS;
      const lastStarted = this.lastCheckStartedAt.get(endpoint.id);
      if (!force && lastStarted !== undefined && now - lastStarted < interval - INTERVAL_SLACK_MS) continue;

      if (this.inFlight.has(endpoint.id)) {
        this.logger.debug(LOG_SOURCE, `Skipping '${endpoint.name}': previous check still running`);
        continue;
      }
      this.inFlight.add(endpoint.id);
      this.lastCheckStartedAt.set(endpoint.id, now);

      const url = isMock ? this.pickMockUrl() : endpoint.url;
      this.logger.debug(LOG_SOURCE, `Checking '${endpoint.name}' -> ${url}`);

      // Each endpoint updates as soon as its own response arrives
      this.ping(url, isMock)
        .then(outcome => this.recordOutcome(endpoint.id, url, outcome))
        .catch(error => this.logger.error(LOG_SOURCE, `Unexpected error checking '${endpoint.name}'`, error))
        .finally(() => this.inFlight.delete(endpoint.id));
    }
  }

  // Called from a card's Clear button: forgets that endpoint's recorded failures.
  // If it is still DOWN, the next failed check starts a new record.
  public clearFailures(id: string): void {
    this.endpointsSignal.update(currentList =>
      currentList.map(endpoint => {
        if (endpoint.id !== id) return endpoint;
        this.logger.info(LOG_SOURCE, `Failure record cleared by user for '${endpoint.name}'`, {
          firstFailedAt: endpoint.firstFailedAt,
          lastFailedAt: endpoint.lastFailedAt,
          failureCount: endpoint.failureCount
        });
        return { ...endpoint, firstFailedAt: null, lastFailedAt: null, failureCount: 0 };
      })
    );
  }

  // Randomly picks a healthy, failing or slow httpbin route
  private pickMockUrl(): string {
    const scenarios = [
      () => `${MOCK_BASE_URL}/status/200`,
      () => `${MOCK_BASE_URL}/status/404`,
      () => `${MOCK_BASE_URL}/delay/${Math.floor(Math.random() * 5) + 1}`
    ];
    return scenarios[Math.floor(Math.random() * scenarios.length)]();
  }

  private async ping(url: string, canReadStatus: boolean): Promise<PingOutcome> {
    const startedAt = performance.now();
    try {
      // httpbin allows CORS so we can read the status code. Real websites usually
      // don't, so they use 'no-cors': the response is opaque, and reaching the
      // server at all counts as UP (network errors / timeouts count as DOWN).
      const response = await fetch(url, {
        mode: canReadStatus ? 'cors' : 'no-cors',
        cache: 'no-store',
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS)
      });
      const latency = Math.round(performance.now() - startedAt);
      const isUp = response.type === 'opaque' || response.ok;
      const detail = response.type === 'opaque' ? 'opaque (no-cors)' : `HTTP ${response.status}`;
      return { status: isUp ? 'UP' : 'DOWN', latency: isUp ? latency : 0, detail };
    } catch (error) {
      // AbortSignal.timeout() rejects with a TimeoutError; CORS/DNS/offline failures are TypeErrors
      const detail = error instanceof DOMException && error.name === 'TimeoutError'
        ? `timeout after ${REQUEST_TIMEOUT_MS} ms`
        : `network error: ${error instanceof Error ? error.message : String(error)}`;
      return { status: 'DOWN', latency: 0, detail };
    }
  }

  private recordOutcome(id: string, url: string, outcome: PingOutcome): void {
    this.endpointsSignal.update(currentList =>
      currentList.map(endpoint => {
        if (endpoint.id !== id) return endpoint;

        const newStatus = outcome.status;
        const newLatency = outcome.latency;
        const summary = `'${endpoint.name}' ${newStatus} (${outcome.detail}, ${newLatency} ms) ${url}`;
        if (endpoint.status !== newStatus) {
          const log = newStatus === 'DOWN' ? this.logger.warn : this.logger.info;
          log.call(this.logger, LOG_SOURCE, `Status changed ${endpoint.status} -> ${summary}`);
        } else {
          this.logger.debug(LOG_SOURCE, summary);
        }
        const updatedHistory = [...endpoint.history];

        // Compress consecutive checks with the same status into one block
        const lastBlock = updatedHistory[updatedHistory.length - 1];
        if (lastBlock && lastBlock.status === newStatus) {
          const checkCount = lastBlock.checkCount + 1;
          updatedHistory[updatedHistory.length - 1] = {
            ...lastBlock,
            averageLatency: Math.round(
              (lastBlock.averageLatency * lastBlock.checkCount + newLatency) / checkCount
            ),
            checkCount
          };
        } else {
          updatedHistory.push({
            status: newStatus,
            averageLatency: newLatency,
            checkCount: 1,
            startedAt: new Date()
          });
          if (updatedHistory.length > MAX_HISTORY_BLOCKS) updatedHistory.shift();
        }

        // 🚨 FAILURE RECORD
        // First and latest failure are kept until the user clears them; recovery never resets them.
        const isDown = newStatus === 'DOWN';
        const now = new Date();
        if (isDown && !endpoint.firstFailedAt) {
          this.logger.warn(LOG_SOURCE, `First failure recorded for '${endpoint.name}'`);
        }

        return {
          ...endpoint,
          // Show which URL was actually hit (the mock endpoint changes it every check)
          url,
          status: newStatus,
          responseTime: newLatency,
          history: updatedHistory,
          firstFailedAt: isDown ? (endpoint.firstFailedAt ?? now) : endpoint.firstFailedAt,
          lastFailedAt: isDown ? now : endpoint.lastFailedAt,
          failureCount: endpoint.failureCount + (isDown ? 1 : 0)
        };
      })
    );
  }
}
