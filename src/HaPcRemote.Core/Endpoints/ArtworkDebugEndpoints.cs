using System.Net;
using System.Text;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using HaPcRemote.Service.Views;

namespace HaPcRemote.Service.Endpoints;

public static class ArtworkDebugEndpoints
{
    public static IEndpointRouteBuilder MapArtworkDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/steam/artwork/debug", async (HttpContext context, ISteamService steamService) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return Results.Text("Forbidden", "text/plain", statusCode: 403);

            var diagnostics = await steamService.GetAllArtworkDiagnosticsAsync();
            var html = BuildOverviewHtml(diagnostics);
            return Results.Content(html, "text/html");
        });

        endpoints.MapGet("/api/steam/artwork/{appId:int}/debug", async (int appId, HttpContext context, ISteamService steamService) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return Results.Text("Forbidden", "text/plain", statusCode: 403);

            var diag = await steamService.GetArtworkDiagnosticsAsync(appId);
            if (diag == null)
                return Results.Text("Steam not available", "text/plain", statusCode: 503);

            var html = BuildDetailHtml(diag);
            return Results.Content(html, "text/html");
        });

        endpoints.MapGet("/api/debug/styles.css", (HttpContext context) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return Results.Text("Forbidden", "text/plain", statusCode: 403);

            var css = EmbeddedResourceHelper.LoadResource("debug.css");
            return Results.Content(css, "text/css");
        });

        return endpoints;
    }

    private static string BuildOverviewHtml(List<ArtworkDiagnostics> diagnostics)
    {
        var contentSb = new StringBuilder();

        if (diagnostics.Count == 0)
        {
            contentSb.AppendLine("<p>No games found. Is Steam installed and running?</p>");
        }

        foreach (var diag in diagnostics)
        {
            var hasArt = diag.ResolvedPath != null;
            var cardClass = hasArt ? "found" : "missing";
            var badgeClass = hasArt ? "badge-found" : "badge-missing";
            var badgeText = hasArt ? "FOUND" : "MISSING";

            contentSb.AppendLine($"<div class=\"game-card {cardClass}\">");
            contentSb.AppendLine("  <div class=\"artwork-preview\">");
            if (hasArt)
                contentSb.AppendLine($"    <img src=\"/api/steam/artwork/{diag.AppId}\" alt=\"{Encode(diag.GameName)}\" />");
            else
                contentSb.AppendLine("    <div class=\"no-img\">No image</div>");
            contentSb.AppendLine("  </div>");

            contentSb.AppendLine("  <div class=\"game-info\">");
            contentSb.Append($"    <h2>{Encode(diag.GameName)} <span class=\"badge {badgeClass}\">{badgeText}</span>");
            if (diag.IsShortcut)
                contentSb.Append(" <span class=\"badge badge-shortcut\">SHORTCUT</span>");
            contentSb.AppendLine("</h2>");

            contentSb.AppendLine("    <div class=\"meta\">");
            contentSb.Append($"      AppId: {diag.AppId} | FileId: {diag.FileId}");
            contentSb.Append($" | <a href=\"/api/steam/artwork/{diag.AppId}/debug\">Full diagnostics</a>");
            if (!string.IsNullOrEmpty(diag.CdnUrl))
                contentSb.Append($" | <a href=\"{diag.CdnUrl}\" target=\"_blank\">CDN fallback</a>");
            contentSb.AppendLine();
            contentSb.AppendLine("    </div>");

            contentSb.AppendLine("    <table class=\"path-table\">");
            contentSb.AppendLine("      <tr><th>Category</th><th>Path</th><th>Status</th></tr>");
            foreach (var p in diag.PathsChecked)
            {
                var cls = p.Exists ? "exists" : "missing-path";
                var status = p.Exists ? FormatSize(p.SizeBytes) : "not found";
                contentSb.AppendLine($"      <tr><td>{Encode(p.Category)}</td><td class=\"{cls}\">{Encode(p.Path)}</td><td class=\"{cls}\">{status}</td></tr>");
            }
            contentSb.AppendLine("    </table>");
            contentSb.AppendLine("  </div>");
            contentSb.AppendLine("</div>");
        }

        var html = EmbeddedResourceHelper.LoadTemplate(
            "artwork-overview.html",
            new Dictionary<string, string> { { "{{CONTENT}}", contentSb.ToString() } }
        );

        return html;
    }

    private static string BuildDetailHtml(ArtworkDiagnostics diag)
    {
        var hasArt = diag.ResolvedPath != null;
        var statusBadge = hasArt
            ? "<span class=\"badge badge-found\">FOUND</span>"
            : "<span class=\"badge badge-missing\">MISSING</span>";

        var contentSb = new StringBuilder();

        // Game Info section
        contentSb.AppendLine("<div class=\"section\">");
        contentSb.AppendLine("  <h2>Game Info</h2>");
        contentSb.AppendLine("  <div class=\"kv\">");
        contentSb.AppendLine($"    <span class=\"label\">AppId</span><span>{diag.AppId}</span>");
        contentSb.AppendLine($"    <span class=\"label\">FileId</span><span>{diag.FileId}</span>");
        contentSb.AppendLine($"    <span class=\"label\">Is Shortcut</span><span>{diag.IsShortcut}</span>");
        contentSb.AppendLine($"    <span class=\"label\">Status</span><span>{statusBadge}</span>");
        contentSb.AppendLine($"    <span class=\"label\">Resolved Path</span><span>{Encode(diag.ResolvedPath ?? "(none)")}</span>");
        if (!string.IsNullOrEmpty(diag.CdnUrl))
            contentSb.AppendLine($"    <span class=\"label\">CDN URL</span><span><a href=\"{diag.CdnUrl}\" target=\"_blank\" style=\"color:#3498db\">{diag.CdnUrl}</a></span>");
        contentSb.AppendLine("  </div>");
        contentSb.AppendLine("</div>");

        // Preview section
        if (hasArt)
        {
            contentSb.AppendLine("<div class=\"section\">");
            contentSb.AppendLine("  <h2>Preview</h2>");
            contentSb.AppendLine("  <div class=\"preview\">");
            contentSb.AppendLine($"    <img src=\"/api/steam/artwork/{diag.AppId}\" alt=\"{Encode(diag.GameName)}\" />");
            contentSb.AppendLine("  </div>");
            contentSb.AppendLine("</div>");
        }

        // Path Resolution Trail
        contentSb.AppendLine("<div class=\"section\">");
        contentSb.AppendLine("  <h2>Path Resolution Trail</h2>");
        contentSb.AppendLine("  <table class=\"path-table\">");
        contentSb.AppendLine("    <tr><th>#</th><th>Category</th><th>Path</th><th>Exists</th><th>Size</th></tr>");

        var i = 1;
        foreach (var p in diag.PathsChecked)
        {
            var cls = p.Exists ? "exists" : "missing-path";
            var existsText = p.Exists ? "YES" : "no";
            var sizeText = p.Exists ? FormatSize(p.SizeBytes) : "-";
            contentSb.AppendLine($"    <tr><td>{i++}</td><td>{Encode(p.Category)}</td><td class=\"{cls}\">{Encode(p.Path)}</td><td class=\"{cls}\">{existsText}</td><td>{sizeText}</td></tr>");
        }

        contentSb.AppendLine("  </table>");
        contentSb.AppendLine("</div>");

        var html = EmbeddedResourceHelper.LoadTemplate(
            "artwork-detail.html",
            new Dictionary<string, string>
            {
                { "{{GAME_NAME}}", Encode(diag.GameName) },
                { "{{CONTENT}}", contentSb.ToString() }
            }
        );

        return html;
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);

    private static string FormatSize(long? bytes)
    {
        if (bytes == null) return "-";
        return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024} KB";
    }
}
