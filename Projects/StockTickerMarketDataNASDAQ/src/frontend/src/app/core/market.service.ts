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

import { Injectable, signal } from '@angular/core';
import * as protobuf from 'protobufjs';
import { WIRE_CODES } from './market.model';

// The websocket to the server. The server pushes one binary protobuf frame every 500 ms; the schema comes from
// /proto/market.proto, the same file the server encodes with. The browser sends its picks and a ping as JSON, and
// drops out after 30 seconds of silence. The server drops a browser that stays silent for three minutes.
export const MAX_PICKS = 100;
const PING_MS = 30_000;
const MIN_RETRY_MS = 1_000;
const MAX_RETRY_MS = 15_000;
// Fields whose rise or fall is colour-coded: last trade, bid and ask.
const PRICE_FIELDS = new Set(['p', 'b', 'a']);
export const PICKS_KEY = "nasdaq.picks";
export type QuoteRow = Record<string, number>;

// The frame as the decoder returns it, with the names and enum text the .proto file gives.
interface WireColumn {
  field: string;
  decimals: number;
  tickerId: number[];
  delta: number[];
}
interface WireFrame {
  seq: number;
  timeMs: number;
  newTickerId: number[];
  newSymbol: string[];
  columns: WireColumn[];
}

@Injectable({ providedIn: 'root' })
export class MarketService {
  readonly connected = signal(false);
  readonly picks = signal<readonly string[]>([]);
  readonly view = signal<readonly string[]>([]);
  readonly quotes = signal<ReadonlyMap<string, QuoteRow>>(new Map());
  // Direction of the last change of each price field: 1 when it rose, -1 when it fell. Kept until the next change.
  readonly directions = signal<ReadonlyMap<string, Readonly<Record<string, number>>>>(new Map());
  readonly lastUpdate = signal<number | null>(null);

  private socket: WebSocket | null = null;
  private frameType: protobuf.Type | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryMs = MIN_RETRY_MS;
  private started = false;
  private paused = false;
  // From the server (Feed:PauseWhenHidden): stop ticking while the tab is hidden. Set by the server only.
  readonly pauseWhenHidden = signal(true);
  // Set by the server at end of day. The page then shows "Refresh to go live" and stays disconnected.
  readonly endOfDay = signal(false);

  // Per connection: the symbol of each ticker id, and the last value received for each id and field. The server
  // sends a delta from that value, so both sides must start empty on every new connection.
  private symbolById = new Map<number, string>();
  private baseById = new Map<number, Map<string, number>>();

  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    document.addEventListener("visibilitychange", () => this.onVisibilityChange());
    this.picks.set(this.loadPicks());
    const root = await protobuf.load('/proto/market.proto');
    this.frameType = root.lookupType('market.Frame');
    this.open();
  }

  isPicked(ticker: string): boolean {
    return this.picks().includes(ticker);
  }

  // Adds or removes a pick. Returns false if the list is full, so the caller can say so.
  togglePick(ticker: string): boolean {
    const current = this.picks();
    if (current.includes(ticker)) {
      this.setPicks(current.filter((t) => t !== ticker));
      return true;
    }
    if (current.length >= MAX_PICKS) return false;
    this.setPicks([...current, ticker]);
    return true;
  }

  private setPicks(tickers: readonly string[]): void {
    this.picks.set(tickers);
    this.savePicks(tickers);
    this.sendState();
  }

  // The Popular tab's list. Streamed to this browser, but it is not the Watch list.
  setView(tickers: readonly string[]): void {
    this.view.set(tickers);
    this.sendState();
  }

  // Every change sends the whole state, so the server always has both lists.
  private sendState(): void {
    this.send({ pick: this.picks(), view: this.view() });
  }

  // The watch list is kept in this browser's localStorage, so it survives a reload. Storage can be unavailable
  // (private mode, blocked storage); the list then simply is not remembered.
  private loadPicks(): string[] {
    try {
      const saved: unknown = JSON.parse(localStorage.getItem(PICKS_KEY) ?? '[]');
      return Array.isArray(saved) ? saved.filter((t): t is string => typeof t === 'string').slice(0, MAX_PICKS) : [];
    } catch {
      return [];
    }
  }

  private savePicks(tickers: readonly string[]): void {
    try {
      localStorage.setItem(PICKS_KEY, JSON.stringify(tickers));
    } catch {
      // Not remembered; the picks still work for this visit.
    }
  }

  private open(): void {
    if (this.paused || this.endOfDay()) return;
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    const socket = new WebSocket(`${scheme}://${location.host}/ws`);
    socket.binaryType = 'arraybuffer';
    this.socket = socket;

    socket.onopen = () => {
      this.connected.set(true);
      this.retryMs = MIN_RETRY_MS;
      this.symbolById = new Map();
      this.baseById = new Map();
      // A new connection starts with no picks on the server, so send them again.
      this.sendState();
      this.pingTimer = setInterval(() => this.send({ ping: 1 }), PING_MS);
    };

    socket.onmessage = (event: MessageEvent<ArrayBuffer | string>) => {
      if (event.data instanceof ArrayBuffer) {
        this.applyFrame(new Uint8Array(event.data));
      } else if (typeof event.data === 'string') {
        this.handleControl(event.data);
      }
    };

    socket.onclose = () => {
      this.connected.set(false);
      this.socket = null;
      if (this.pingTimer !== null) clearInterval(this.pingTimer);
      this.pingTimer = null;
      // Paused, or the day has ended: do not reconnect. A refresh (or returning to the tab) brings the page back.
      if (this.paused || this.endOfDay()) return;
      this.retryTimer = setTimeout(() => this.open(), this.retryMs);
      this.retryMs = Math.min(this.retryMs * 2, MAX_RETRY_MS);
    };
  }

  // Server control messages. Only the end-of-day message exists.
  private handleControl(text: string): void {
    try {
      const msg = JSON.parse(text) as { control?: string };
      if (msg.control === "endOfDay") {
        this.endOfDay.set(true);
        this.clearRetry();
      } else if (msg.control === "config") {
        this.pauseWhenHidden.set((msg as { pauseWhenHidden?: boolean }).pauseWhenHidden !== false);
      }
    } catch {
      // Not a control message; ignore it.
    }
  }

  private onVisibilityChange(): void {
    if (document.hidden) {
      if (this.pauseWhenHidden()) this.pause();
    } else if (this.paused && !this.endOfDay()) {
      this.resume();
    }
  }

  private pause(): void {
    this.paused = true;
    this.clearRetry();
    this.socket?.close();
  }

  private resume(): void {
    this.paused = false;
    this.open();
  }

  private clearRetry(): void {
    if (this.retryTimer !== null) clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  private send(payload: object): void {
    if (this.socket?.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify(payload));
    }
  }

  private applyFrame(bytes: Uint8Array): void {
    if (this.frameType === null) return;
    const decoded = this.frameType.decode(bytes);
    const frame = this.frameType.toObject(decoded, {
      longs: Number,
      enums: String,
      arrays: true,
    }) as WireFrame;

    frame.newTickerId.forEach((id, i) => {
      this.symbolById.set(id, frame.newSymbol[i]);
      this.baseById.set(id, new Map());
    });

    const next = new Map(this.quotes());
    const nextDirections = new Map(this.directions());
    for (const column of frame.columns) {
      const code = WIRE_CODES[column.field];
      if (code === undefined) continue;
      // protobuf leaves out a field whose value is 0, so a missing decimals means 0 decimal places.
      const divisor = 10 ** (column.decimals ?? 0);

      column.tickerId.forEach((id, j) => {
        const symbol = this.symbolById.get(id);
        const base = this.baseById.get(id);
        if (symbol === undefined || base === undefined) return;

        const scaled = (base.get(code) ?? 0) + column.delta[j];
        base.set(code, scaled);
        const value = scaled / divisor;

        const row: QuoteRow = { ...(next.get(symbol) ?? {}) };
        const previous = row[code];
        row[code] = value;
        next.set(symbol, row);

        if (PRICE_FIELDS.has(code) && previous !== undefined && value !== previous) {
          const moves = { ...(nextDirections.get(symbol) ?? {}) };
          moves[code] = value > previous ? 1 : -1;
          nextDirections.set(symbol, moves);
        }
      });
    }

    this.quotes.set(next);
    this.directions.set(nextDirections);
    this.lastUpdate.set(frame.timeMs);
  }
}
