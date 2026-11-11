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

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ServiceStatus, UniversePage } from './market.model';

// HTTP calls to the server. Prices do not come from here; they come over the websocket.
@Injectable({ providedIn: 'root' })
export class UniverseApi {
  private readonly http = inject(HttpClient);

  search(query: string, page: number, pageSize: number, set: 'all' | 'popular' = 'all'): Observable<UniversePage> {
    const params = new HttpParams().set('q', query).set('page', page).set('pageSize', pageSize).set('set', set);
    return this.http.get<UniversePage>('/api/universe', { params });
  }

  status(): Observable<ServiceStatus> {
    return this.http.get<ServiceStatus>('/api/status');
  }
}
