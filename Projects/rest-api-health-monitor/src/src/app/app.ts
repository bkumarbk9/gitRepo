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

import { Component, signal, computed, inject, OnInit, OnDestroy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { EndpointState } from './core/services/endpoint-state';
import { ApiEndpoint, PingResult } from './models/endpoint.model';
import { timer, Subscription } from 'rxjs';
import { DatePipe } from '@angular/common';

@Component({
  imports: [RouterOutlet,DatePipe],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})

export class App implements OnInit, OnDestroy {
  protected readonly title = signal('rest-api-health-monitor');
  private endpointState = inject(EndpointState);
  private timerSubscription?: Subscription;

  public endpoints = this.endpointState.endpoints;
  public totalUp = this.endpointState.totalUp;       
  public totalDown = this.endpointState.totalDown; 

  // When checked, the grid only shows endpoints whose latest check is DOWN
  public showFailedOnly = signal(false);
  public visibleEndpoints = computed(() =>
    this.showFailedOnly()
      ? this.endpoints().filter(item => item.status === 'DOWN')
      : this.endpoints()
  );

  public onShowFailedOnlyChange(event: Event): void {
    this.showFailedOnly.set((event.target as HTMLInputElement).checked);
  }

   public onCheckNowClick(): void {
    this.endpointState.checkHealth(true);
  }

   ngOnInit(): void {
    // Start a background loop: delay 0 seconds, then tick every 3 seconds.
    // Each tick only checks endpoints whose own interval has passed (see EndpointState).
    this.timerSubscription = timer(0, 3000).subscribe(() => {
      this.endpointState.checkHealth();
    });
  }

  // Bar height relative to the slowest block in this endpoint's history
  public latencyPercent(item: ApiEndpoint, ping: PingResult): number {
    if (ping.status === 'DOWN') return 0;
    const maxLatency = Math.max(...item.history.map(h => h.averageLatency), 1);
    return (ping.averageLatency / maxLatency) * 100;
  }

  ngOnDestroy(): void {
    //Clean up the timer when the app closes to prevent memory leaks
    if (this.timerSubscription) {
      this.timerSubscription.unsubscribe();
    }
  }
}
