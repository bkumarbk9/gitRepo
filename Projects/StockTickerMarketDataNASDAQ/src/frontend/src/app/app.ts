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

import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, of, switchMap, timer } from 'rxjs';
import { ServiceStatus } from './core/market.model';
import { MarketService } from './core/market.service';
import { UniverseApi } from './core/universe.api';
import { ViewportService } from './core/viewport.service';

// Shell: navigation and a status bar. The status bar reads /api/status every 3 seconds, so it shows the simulated
// session even when no prices are flowing. Prices arrive over the websocket, not from this poll.
// Two layouts: on a phone, navigation sits in a bottom bar and the status is one compact line; on a desktop, the
// navigation is at the top and the status bar lists every value.
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Fixed in the top-left corner, so it stays in view while a table scrolls. -->
    <div class="live-badge" role="status" [class.off]="!market.connected() && market.everConnected()" [class.loading]="!market.everConnected()">
      @if (market.connected()) {
        <span class="dot" aria-hidden="true"></span> live
      } @else if (market.everConnected()) {
        Refresh to start
      } @else {
        <span class="spinner" aria-hidden="true"></span> Loading<span class="dots" aria-hidden="true"></span>
      }
    </div>
    @if (market.endOfDay()) {
      <p class="eod" role="alert">Refresh to go live</p>
    }
    @if (viewport.isMobile()) {
      <!-- Mobile: a compact title, the content, and a bottom bar with large tap targets. -->
      <header class="mobile-head">
        <h1>NASDAQ live</h1>
      </header>

      <main>
        <router-outlet />
      </main>

      <nav class="bottom-bar">
        <a routerLink="/universe" routerLinkActive="active">Universe</a>
        <a routerLink="/popular" routerLinkActive="active">Popular</a>
        <a routerLink="/watch" routerLinkActive="active">Watch ({{ market.picks().length }})</a>
      </nav>

      <footer class="mobile-status" [class.bad]="!market.connected()">
        Simulated · session {{ status()?.session ?? '—' }} · minute {{ status()?.sessionMinute ?? '—' }} · {{ status()?.priced ?? 0 }} priced
      </footer>
    } @else {
      <!-- Desktop: a top bar with navigation, the content, and a full status bar. -->
      <header>
        <h1>NASDAQ live prices (simulated)</h1>
        <nav>
          <a routerLink="/universe" routerLinkActive="active">Universe</a>
          <a routerLink="/popular" routerLinkActive="active">Popular</a>
          <a routerLink="/watch" routerLinkActive="active">Watch ({{ market.picks().length }})</a>
        </nav>
      </header>

      <main>
        <router-outlet />
      </main>

      <footer [class.bad]="!market.connected()">
        <span>Browser link: {{ market.connected() ? 'live' : 'Refresh to start' }}</span>
        <span>Prices: simulated</span>
        <span>Session: {{ status()?.session ?? '—' }}</span>
        <span>Minute: {{ status()?.sessionMinute ?? '—' }} of 390</span>
        <span>Universe: {{ status()?.universe ?? '—' }}</span>
        <span>Priced: {{ status()?.priced ?? 0 }}</span>
      </footer>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 100vh; font-family: system-ui, sans-serif; color: #1c1c1c; }
    .live-badge { position: fixed; top: 0.5rem; right: 0.6rem; z-index: 30; display: flex; align-items: center; gap: 0.4rem;
                  padding: 0.2rem 0.7rem; border-radius: 999px; background: #fff; box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
                  font-weight: 700; font-size: 0.9rem; color: #1a7f37; }
    .live-badge.off { color: #c62828; }
    .dot { width: 0.6rem; height: 0.6rem; border-radius: 50%; background: #1a7f37; animation: blink 2.4s ease-in-out infinite; }
    @keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.2; } }
    .live-badge.loading { color: #8a6d00; }
    .spinner { width: 0.8rem; height: 0.8rem; border: 2px solid #d0a000; border-top-color: transparent; border-radius: 50%; animation: spin 0.9s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .dots::after { content: ''; display: inline-block; width: 1.4em; text-align: left; animation: dots 1.2s steps(4, end) infinite; }
    @keyframes dots { 0% { content: ''; } 25% { content: '.'; } 50% { content: '..'; } 75% { content: '...'; } }
    h1 { font-size: 1.1rem; margin: 0; }
    main { flex: 1; padding: 1rem; min-width: 0; }
    .bad { background: #fdecec; }
    .eod { margin: 0; padding: 0.4rem 0.8rem; background: #fdecec; color: #c62828; font-weight: 700; text-align: center; }

    /* Desktop */
    header { display: flex; align-items: center; gap: 2rem; padding: 0.6rem 1rem; border-bottom: 1px solid #ddd; }
    header nav { display: flex; gap: 1rem; }
    header nav a { text-decoration: none; color: #444; padding: 0.3rem 0.5rem; border-radius: 4px; }
    header nav a.active { background: #1f5fbf; color: #fff; }
    footer { display: flex; flex-wrap: wrap; gap: 1.2rem; padding: 0.5rem 1rem; border-top: 1px solid #ddd; background: #f7f7f7; font-size: 0.85rem; color: #333; }

    /* Mobile */
    .mobile-head { display: flex; justify-content: space-between; align-items: center; padding: 0.6rem 0.9rem; border-bottom: 1px solid #ddd; }
    .mobile-head .link { font-size: 0.8rem; color: #2a7a2a; }
    .mobile-head .link.bad { color: #a40000; }
    .bottom-bar { position: sticky; bottom: 0; display: grid; grid-template-columns: 1fr 1fr 1fr; background: #fff; border-top: 1px solid #ddd; }
    .bottom-bar a { text-align: center; text-decoration: none; color: #444; padding: 0.9rem 0.4rem; min-height: 3rem; font-weight: 600; }
    .bottom-bar a.active { color: #1f5fbf; border-top: 3px solid #1f5fbf; }
    .mobile-status { padding: 0.4rem 0.8rem; font-size: 0.78rem; color: #333; background: #f7f7f7; border-top: 1px solid #ddd; }
  `,
})
export class App implements OnInit {
  protected readonly market = inject(MarketService);
  protected readonly viewport = inject(ViewportService);
  private readonly api = inject(UniverseApi);

  private readonly polled = toSignal(
    timer(0, 3000).pipe(
      switchMap(() => this.api.status().pipe(catchError(() => of(null)))),
    ),
    { initialValue: null as ServiceStatus | null },
  );

  protected readonly status = computed(() => this.polled());

  ngOnInit(): void {
    this.market.start();
  }
}
