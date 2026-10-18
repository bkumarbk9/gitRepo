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
