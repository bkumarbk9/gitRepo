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

import { TestBed } from '@angular/core/testing';
import { afterEach, vi } from 'vitest';
import { EndpointState } from './endpoint-state';

// Fake fetch: serves endpoints.json, then answers each health check from `pingResults`
// ('up' -> HTTP 200, 'down' -> network error) keyed by URL.
const CONFIG = [
  { id: '2', name: 'Site A', url: 'https://a.example' },
  { id: '3', name: 'Site B', url: 'https://b.example' }
];
let pingResults: Record<string, 'up' | 'down'>;

function stubFetch(): void {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === 'endpoints.json') return { ok: true, status: 200, json: async () => CONFIG };
    if (pingResults[url] === 'down') throw new TypeError('Failed to fetch');
    return { ok: true, status: 200, type: 'basic' };
  }));
}

describe('EndpointState', () => {
  let service: EndpointState;

  const find = (id: string) => service.endpoints().find(e => e.id === id)!;

  // Forces a check of every endpoint and waits until each has recorded one more result
  async function runCheck(): Promise<void> {
    const before = service.endpoints().map(e => e.history.reduce((n, h) => n + h.checkCount, 0));
    service.checkHealth(true);
    await vi.waitFor(() => {
      const after = service.endpoints().map(e => e.history.reduce((n, h) => n + h.checkCount, 0));
      expect(after.every((n, i) => n === before[i] + 1)).toBe(true);
    });
  }

  beforeEach(async () => {
    pingResults = { 'https://a.example': 'up', 'https://b.example': 'up' };
    stubFetch();
    TestBed.configureTestingModule({});
    service = TestBed.inject(EndpointState);
    await service.loadEndpoints();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
    expect(service.endpoints().length).toBe(2);
  });

  it('records the first failure and updates the latest failure on each DOWN check', async () => {
    pingResults['https://a.example'] = 'down';
    await runCheck();
    const first = find('2').firstFailedAt;
    expect(first).toBeInstanceOf(Date);
    expect(find('2').lastFailedAt).toBe(first);
    expect(find('2').failureCount).toBe(1);

    await runCheck();
    expect(find('2').firstFailedAt).toBe(first);
    expect(find('2').lastFailedAt).not.toBe(first);
    expect(find('2').failureCount).toBe(2);
  });

  it('keeps the failure record after the endpoint recovers', async () => {
    pingResults['https://a.example'] = 'down';
    await runCheck();
    const { firstFailedAt, lastFailedAt } = find('2');

    pingResults['https://a.example'] = 'up';
    for (let i = 0; i < 5; i++) await runCheck();

    expect(find('2').status).toBe('UP');
    expect(find('2').firstFailedAt).toBe(firstFailedAt);
    expect(find('2').lastFailedAt).toBe(lastFailedAt);
    expect(find('2').failureCount).toBe(1);
  });

  it('clearFailures only clears the chosen endpoint', async () => {
    pingResults = { 'https://a.example': 'down', 'https://b.example': 'down' };
    await runCheck();

    service.clearFailures('2');

    expect(find('2').firstFailedAt).toBeNull();
    expect(find('2').lastFailedAt).toBeNull();
    expect(find('2').failureCount).toBe(0);
    expect(find('3').firstFailedAt).toBeInstanceOf(Date);
    expect(find('3').failureCount).toBe(1);
  });

  it('starts a new record when an endpoint fails again after being cleared', async () => {
    pingResults['https://a.example'] = 'down';
    await runCheck();
    const oldFirst = find('2').firstFailedAt;
    service.clearFailures('2');

    await runCheck();
    expect(find('2').firstFailedAt).toBeInstanceOf(Date);
    expect(find('2').firstFailedAt).not.toBe(oldFirst);
    expect(find('2').failureCount).toBe(1);
  });
});
