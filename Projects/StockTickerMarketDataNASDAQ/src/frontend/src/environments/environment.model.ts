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

export interface AppEnvironment {
  // 'local': the browser opens this server's /ws. 'web-pubsub': the browser joins an Azure Web PubSub group.
  transport: 'local' | 'web-pubsub';
  // Where /api and /proto are served. Empty means the same site (the local dev proxy).
  apiBase: string;
  // The Web PubSub group the server publishes to. Used only with 'web-pubsub'.
  group: string;
}
