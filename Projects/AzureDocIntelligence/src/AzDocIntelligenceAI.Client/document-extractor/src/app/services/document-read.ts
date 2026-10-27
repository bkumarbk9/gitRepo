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

import { Service } from '@angular/core';
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root' // Tells Angular to share this single instance app-wide
})

export class DocumentRead {
 private http = inject(HttpClient); 
  // Relative: served by the same host in Azure; proxied to Azure by ng serve (proxy.conf.json)
  private apiUrl = '/api/docintel/analyze';

  /**
   * Encapsulates raw binary document files into form payloads
   */
  uploadAndConvert(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    // Browser automatically defines correct boundary states for multipart uploads
    return this.http.post<any>(this.apiUrl, formData);
  }
}
