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

import { Injectable, isDevMode } from '@angular/core';

export type LogLevel = 'debug' | 'info' | 'warn' | 'error' | 'off';

const LEVEL_ORDER: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3, off: 4 };
const STORAGE_KEY = 'healthMonitor.logLevel';
const MAX_BUFFERED_ENTRIES = 500;

interface LogEntry {
  time: string;
  level: Exclude<LogLevel, 'off'>;
  source: string;
  message: string;
  data?: unknown;
}

@Injectable({
  providedIn: 'root'
})
export class Logger {
  private level: LogLevel = this.initialLevel();

  // Recent entries (at every level) so they can be dumped even if the console was filtered
  private buffer: LogEntry[] = [];

  constructor() {
    // Console helpers for debugging a running app:
    //   healthMonitor.setLogLevel('debug')  -> change level (remembered across reloads)
    //   healthMonitor.logs()                -> table of recent entries
    (window as any).healthMonitor = {
      setLogLevel: (level: LogLevel) => this.setLevel(level),
      logs: () => console.table(this.buffer),
      exportLogs: () => JSON.stringify(this.buffer, null, 2)
    };
  }

  public debug(source: string, message: string, data?: unknown): void { this.write('debug', source, message, data); }
  public info(source: string, message: string, data?: unknown): void { this.write('info', source, message, data); }
  public warn(source: string, message: string, data?: unknown): void { this.write('warn', source, message, data); }
  public error(source: string, message: string, data?: unknown): void { this.write('error', source, message, data); }

  public setLevel(level: LogLevel): void {
    this.level = level;
    try {
      localStorage.setItem(STORAGE_KEY, level);
    } catch {
      // Storage can be blocked (private mode etc.); the level still applies for this session
    }
    console.info(`[HealthMonitor] log level set to '${level}'`);
  }

  // Priority: ?logLevel=debug in the URL, then saved setting, then dev/prod default
  private initialLevel(): LogLevel {
    const fromUrl = new URLSearchParams(window.location.search).get('logLevel');
    if (fromUrl && fromUrl in LEVEL_ORDER) return fromUrl as LogLevel;
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved && saved in LEVEL_ORDER) return saved as LogLevel;
    } catch {
      // Ignore blocked storage
    }
    return isDevMode() ? 'debug' : 'warn';
  }

  private write(level: Exclude<LogLevel, 'off'>, source: string, message: string, data?: unknown): void {
    const time = new Date().toISOString();
    this.buffer.push({ time, level, source, message, data });
    if (this.buffer.length > MAX_BUFFERED_ENTRIES) this.buffer.shift();

    if (LEVEL_ORDER[level] < LEVEL_ORDER[this.level]) return;
    const prefix = `${time.substring(11, 23)} [HealthMonitor:${source}]`;
    const args = data === undefined ? [prefix, message] : [prefix, message, data];
    console[level](...args);
  }
}
