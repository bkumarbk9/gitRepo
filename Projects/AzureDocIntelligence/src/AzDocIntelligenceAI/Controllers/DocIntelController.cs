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

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BK_AzDocIntelligenceAI.Controllers
{
    [ApiController]
    [Route("api/docintel")]
    public class DocIntelController : ControllerBase
    {
        private const long MaxFileBytes = 2 * 1024 * 1024;                   // 2 MB
        private static readonly string[] sAllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];

        private readonly ILogger<DocIntelController> mLogger;
        private readonly AzDocIntel mDocIntel;

        public DocIntelController(ILogger<DocIntelController> logger, AzDocIntel docIntelService)
        {
            mLogger = logger;
            mDocIntel = docIntelService;
        }

        [HttpPost("analyze")]
        [EnableRateLimiting("per-ip-daily")]
        [RequestSizeLimit(3 * 1024 * 1024)]                                  // whole request incl. multipart overhead
        [RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
        public async Task<IActionResult> Analyze(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                mLogger.LogWarning("Analyze request rejected: no file uploaded");
                return BadRequest("No file uploaded.");
            }

            // Log size and extension only; file names and contents may contain personal data
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            mLogger.LogInformation("Analyze request received: {FileSizeBytes} bytes, extension {FileExtension}", file.Length, extension);

            if (file.Length > MaxFileBytes)
            {
                mLogger.LogWarning("Analyze request rejected: file size {FileSizeBytes} bytes exceeds limit of {MaxFileBytes} bytes",
                    file.Length, MaxFileBytes);
                return StatusCode(StatusCodes.Status413PayloadTooLarge, "File too large. Maximum is 2 MB.");
            }

            if (!sAllowedExtensions.Contains(extension))
            {
                mLogger.LogWarning("Analyze request rejected: unsupported file extension {FileExtension}", extension);
                return BadRequest("Unsupported file type. Allowed: PDF, JPG, PNG, TIFF, BMP.");
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);

            string json = await mDocIntel.GetDataAsync(ms.ToArray(), cancellationToken);
            if (string.IsNullOrEmpty(json))
            {
                mLogger.LogWarning("Analyze request failed; returning 502 to the client");
                return StatusCode(StatusCodes.Status502BadGateway, "Document Intelligence call failed - check container logs.");
            }

            mLogger.LogInformation("Analyze request completed successfully");
            return Content(json, "application/json");
        }
    }
}
