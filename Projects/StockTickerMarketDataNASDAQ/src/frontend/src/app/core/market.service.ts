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
import { WebPubSubClient } from '@azure/web-pubsub-client';
import { WIRE_CODES } from './market.model';
import { environment } from '../../environments/environment';

// The live feed has two transports, chosen by environment.transport (the config file for the build):
//  - 'local': a websocket to this server's /ws. The server sends one binary protobuf frame every 500 ms, a JSON ping
//    keeps the socket alive, and the server sends control messages as text.
//  - 'web-pubsub': an Azure Web PubSub group. The server opens a session (/api/negotiate), and the browser sends its
//    picks to /api/picks with the session id. Each group message starts with a kind byte: 0 = delta, 1 = snapshot.
// In both, the schema comes from /proto/market.proto, the same file the server encodes with.
export const MAX_PICKS = 100;
const PING_MS = 30_000;
const MIN_RETRY_MS = 1_000;
const MAX_RETRY_MS = 15_000;
const KIND_DELTA = 0;
const KIND_SNAPSHOT = 1;
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
  private cloud: WebPubSubClient | null = null;
  private sessionId: string | null = null;
  private frameType: protobuf.Type | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryMs = MIN_RETRY_MS;
  private started = false;
  private paused = false;
  // From the server (Feed:PauseWhenHidden): stop ticking while the tab is hidden. Set by the server only.
  readonly pauseWhenHidden = signal(true);
  // Set by the server at end of day, and when a cloud session ends. The page then shows "Refresh to go live" and stays
  // disconnected. A refresh starts a new session.
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
    // Fetched here, not with protobuf.load: protobufjs resolves a full https:// address as a relative path in the browser.
    const response = await fetch(`${environment.apiBase}/proto/market.proto`);
    if (!response.ok) throw new Error(`market.proto: ${response.status}`);
    const { root } = protobuf.parse(await response.text());
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
    if (environment.transport === 'web-pubsub') {
      void this.sendPicks();
      return;
    }
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
    if (environment.transport === 'web-pubsub') {
      void this.openCloud();
      return;
    }
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

  // Cloud: opens a session, then joins the group. A failed negotiate is retried, as the socket is.
  private async openCloud(): Promise<void> {
    try {
      const response = await fetch(`${environment.apiBase}/api/negotiate`);
      if (!response.ok) throw new Error(`negotiate failed: ${response.status}`);
      const { url, sessionId } = (await response.json()) as { url: string; sessionId: string };
      if (this.paused || this.endOfDay()) return;

      this.sessionId = sessionId;
      // autoReconnect is off on purpose: a reconnect would open a new session and restart the session limit.
      const client = new WebPubSubClient(url, { autoReconnect: false });
      this.cloud = client;

      client.on('connected', () => {
        this.connected.set(true);
        this.retryMs = MIN_RETRY_MS;
        this.symbolById = new Map();
        this.baseById = new Map();
        client.joinGroup(environment.group).catch(() => this.stopCloud());
        void this.sendPicks();
      });
      client.on('group-message', (event) => {
        const message = event.message;
        if (message.dataType === 'binary' && message.data instanceof ArrayBuffer) {
          this.applyEnvelope(new Uint8Array(message.data));
        } else if (message.dataType === 'text' && typeof message.data === 'string') {
          this.handleControl(message.data);
        }
      });
      client.on('disconnected', () => this.onCloudClosed(client));

      await client.start();
    } catch {
      this.retryLater();
    }
  }

  // The server closed the session (its time is up) or the network dropped. Either way, a refresh starts a new session.
  // The page does not reconnect by itself, because a new session would restart the session limit.
  private onCloudClosed(client: WebPubSubClient): void {
    if (client !== this.cloud) return;
    this.cloud = null;
    this.connected.set(false);
    this.endOfDay.set(true);
  }

  private stopCloud(): void {
    const client = this.cloud;
    this.cloud = null;
    client?.stop();
  }

  private retryLater(): void {
    if (this.paused || this.endOfDay()) return;
    this.retryTimer = setTimeout(() => this.open(), this.retryMs);
    this.retryMs = Math.min(this.retryMs * 2, MAX_RETRY_MS);
  }

  // Sends the picks for this cloud session. The server ignores them if the session has ended.
  private async sendPicks(): Promise<void> {
    if (this.sessionId === null) return;
    try {
      await fetch(`${environment.apiBase}/api/picks`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Session-Id': this.sessionId },
        body: JSON.stringify({ picks: this.picks() }),
      });
    } catch {
      // Not sent; the next change of picks sends them again.
    }
  }

  // Server control messages: end of day, and the pause setting.
  private handleControl(text: string): void {
    try {
      const msg = JSON.parse(text) as { control?: string };
      if (msg.control === "endOfDay") {
        this.endOfDay.set(true);
        this.clearRetry();
        this.stopCloud();
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
    this.stopCloud();
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

  // A cloud group message: one kind byte, then the protobuf frame. A snapshot resets the baseline, so a browser that
  // joined late starts from the current values. Deltas for tickers it has not been told about are skipped.
  private applyEnvelope(bytes: Uint8Array): void {
    if (bytes[0] === KIND_SNAPSHOT) {
      this.symbolById = new Map();
      this.baseById = new Map();
    } else if (bytes[0] !== KIND_DELTA) {
      return;
    }
    this.applyFrame(bytes.subarray(1));
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
