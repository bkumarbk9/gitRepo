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

using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace BK_AzDocIntelligenceAI
{
    public class AzDocIntel
    {
        private const string ModelId = "prebuilt-layout";
        private const string PageRange = "1-3";   // limits billed pages; applies to PDF/TIFF (images are always 1 page)

        private static readonly JsonSerializerOptions sJsonOptions = new() { WriteIndented = true };

        private readonly ILogger<AzDocIntel> mLogger;
        private readonly DocumentIntelligenceClient mClient;

        public AzDocIntel(ILogger<AzDocIntel> logger, DocumentIntelligenceClient client)
        {
            mLogger = logger;
            mClient = client;
        }

        /// <summary>
        /// Analyzes a document with the Document Intelligence layout model.
        /// Returns the per-page result as JSON, or an empty string if the analysis failed (details are logged).
        /// </summary>
        public async Task<string> GetDataAsync(byte[] paramScannedFileData, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var options = new AnalyzeDocumentOptions(ModelId, BinaryData.FromBytes(paramScannedFileData))
                {
                    Pages = PageRange
                };

                mLogger.LogInformation(
                    "Sending document to Document Intelligence: model {ModelId}, pages {PageRange}, size {FileSizeBytes} bytes",
                    ModelId, PageRange, paramScannedFileData.Length);

                Operation<AnalyzeResult> operation =
                    await mClient.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
                AnalyzeResult result = operation.Value;

                mLogger.LogInformation(
                    "Document Intelligence analysis completed in {ElapsedMs} ms: {PageCount} pages, {ParagraphCount} paragraphs, operation {OperationId}",
                    stopwatch.ElapsedMilliseconds, result.Pages.Count, result.Paragraphs.Count, operation.Id);

                var cleanJson = result.Pages.Select(page => new
                {
                    PageNumber = page.PageNumber,
                    Angle = page.Angle,
                    Width = page.Width,
                    Height = page.Height,
                    Unit = page.Unit?.ToString(),           // LengthUnit has no public properties; serialize as "inch"/"pixel"

                    // Paragraphs that belong to this page
                    Content = result.Paragraphs
                        .Where(para => para.BoundingRegions.Any(br => br.PageNumber == page.PageNumber))
                        .Select(para => para.Content)
                        .ToList(),

                    AvgConfidence = page.Words.Any()
                        ? page.Words.Average(w => w.Confidence)
                        : 0
                });

                string json = JsonSerializer.Serialize(cleanJson, sJsonOptions);
                mLogger.LogDebug("Serialized analysis result: {ResponseLength} characters", json.Length);

                return json;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The client disconnected; nobody is waiting for the result
                mLogger.LogInformation("Document analysis cancelled by the client after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            }
            catch (RequestFailedException ex)
            {
                // Azure service errors: invalid key/endpoint, unsupported or corrupt file, throttling, outages
                mLogger.LogError(ex,
                    "Document Intelligence request failed after {ElapsedMs} ms: HTTP {StatusCode}, error code {ErrorCode}",
                    stopwatch.ElapsedMilliseconds, ex.Status, ex.ErrorCode);
            }
            catch (Exception ex)
            {
                mLogger.LogError(ex, "Unexpected error during document analysis after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            }

            return string.Empty;
        }
    }
}
