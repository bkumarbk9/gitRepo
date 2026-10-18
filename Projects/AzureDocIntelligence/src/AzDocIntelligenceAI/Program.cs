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

using System.Threading.RateLimiting;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace BK_AzDocIntelligenceAI
{
    public class Program
    {
        private const int PerIpDailyLimit = 100;
        private const int GlobalDailyLimit = 1000;
        private const string EndpointSetting = "DOC_INTEL_ENDPOINT";
        private const string ApiKeySetting = "DOC_INTEL_APIKEY";
        private const string CorsOriginsSetting = "CORS_ALLOWED_ORIGINS";
        private const string DefaultCorsOrigins = "http://localhost:4200";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;      // adds RequestId/TraceId so all log lines of one request can be correlated
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
            // Add services to the container.

            builder.Services.AddControllers();

            // Browser clients (e.g. the Angular app) need CORS; comma-separated list, e.g. "http://localhost:4200,https://myclient.com"
            string[] corsOrigins = (builder.Configuration[CorsOriginsSetting] ?? DefaultCorsOrigins)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
                .WithOrigins(corsOrigins)
                .WithMethods("POST")
                .AllowAnyHeader()));
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = 1;          // trust only the last hop (added by ACA ingress), so users can't spoof it
                o.KnownIPNetworks.Clear();   // ACA ingress IPs aren't fixed
                o.KnownProxies.Clear();
            });

            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                o.OnRejected = (ctx, _) =>
                {
                    if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                        ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();

                    var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("BK_AzDocIntelligenceAI.RateLimiting");
                    logger.LogWarning("Rate limit exceeded for client {ClientIp} on {Path}",
                        ctx.HttpContext.Connection.RemoteIpAddress, ctx.HttpContext.Request.Path);
                    return ValueTask.CompletedTask;
                };

                // 100 requests per IP per day (applied on the endpoint)
                o.AddPolicy("per-ip-daily", http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PerIpDailyLimit,
                            Window = TimeSpan.FromDays(1),
                            QueueLimit = 0
                        }));

                // 1,000 requests per day across all users, analyze endpoint only (health checks stay unlimited)
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    http.Request.Path.StartsWithSegments("/api/docintel")
                        ? RateLimitPartition.GetFixedWindowLimiter("all-users", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = GlobalDailyLimit,
                            Window = TimeSpan.FromDays(1),
                            QueueLimit = 0
                        })
                        : RateLimitPartition.GetNoLimiter("unlimited"));
            });
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            // One client for the app's lifetime: Azure SDK clients are thread-safe and designed to be reused
            builder.Services.AddSingleton(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                string endpoint = GetRequiredSetting(config, EndpointSetting);
                string apiKey = GetRequiredSetting(config, ApiKeySetting);
                return new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            });
            builder.Services.AddSingleton<AzDocIntel>();

            var app = builder.Build();

            // Create the client now, so missing or invalid configuration stops the app at startup
            // instead of failing on the first upload
            try
            {
                app.Services.GetRequiredService<DocumentIntelligenceClient>();
            }
            catch (Exception ex)
            {
                app.Logger.LogCritical(ex, "Document Intelligence client could not be created; check {EndpointSetting} and {ApiKeySetting}",
                    EndpointSetting, ApiKeySetting);
                throw;
            }

            app.UseForwardedHeaders();
            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseDefaultFiles();       // serves the Angular client (wwwroot/index.html) at the site root
            app.UseStaticFiles();
            app.UseCors();               // before the rate limiter, so preflight requests don't count against the limits
            app.UseRateLimiter();
            app.MapControllers();

            app.Logger.LogInformation(
                "Rate limits: {PerIpDailyLimit} requests per IP per day, {GlobalDailyLimit} requests per day in total",
                PerIpDailyLimit, GlobalDailyLimit);

            app.Run();
        }

        private static string GetRequiredSetting(IConfiguration config, string name)
        {
            string? value = config[name];
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Configuration value '{name}' is not set.");
            return value;
        }
    }
}
