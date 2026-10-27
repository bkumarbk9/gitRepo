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

export interface PingResult {
  status: 'UP' | 'DOWN';
  averageLatency: number;
  checkCount: number;
  startedAt: Date;
}

// Shape of each entry in public/endpoints.json
export interface EndpointConfig {
  id: string;
  name: string;
  url: string;
}

export interface ApiEndpoint {
  id: string;
  name: string;
  url: string;
  status: 'UP' | 'DOWN' | 'PENDING';
  responseTime: number;
  history: PingResult[];
  incidentStartedAt: Date | null; // Tracks the true start of a multi-flip unstable incident
  lastFailedAt: Date | null;      // Most recent DOWN check; kept after the incident resolves
}
