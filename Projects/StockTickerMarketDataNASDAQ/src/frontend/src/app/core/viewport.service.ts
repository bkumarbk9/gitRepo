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

import { Injectable, OnDestroy, signal } from '@angular/core';

// Screens narrower than this get the mobile layout of each view.
export const MOBILE_QUERY = '(max-width: 720px)';

// Tells every view which layout to show. Views pick a mobile or a desktop variant from isMobile().
@Injectable({ providedIn: 'root' })
export class ViewportService implements OnDestroy {
  private readonly query: MediaQueryList | null =
    typeof matchMedia === 'function' ? matchMedia(MOBILE_QUERY) : null;

  readonly isMobile = signal(this.query?.matches ?? false);

  private readonly onChange = (event: MediaQueryListEvent) => this.isMobile.set(event.matches);

  constructor() {
    this.query?.addEventListener('change', this.onChange);
  }

  ngOnDestroy(): void {
    this.query?.removeEventListener('change', this.onChange);
  }
}
