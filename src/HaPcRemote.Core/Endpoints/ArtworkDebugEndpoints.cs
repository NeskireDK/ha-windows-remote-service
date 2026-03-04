using System.Net;
using System.Text;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;

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

        return endpoints;
    }

    private const string CssBlock = """
<style>
  :root { --bg: #1a1a2e; --surface: #16213e; --border: #0f3460; --text: #eee; --muted: #999; --green: #2ecc71; --red: #e74c3c; --font: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, monospace; }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: var(--font); background: var(--bg); color: var(--text); padding: 24px; max-width: 1200px; margin: 0 auto; }
  h1 { font-size: 1.5rem; margin-bottom: 4px; }
  .subtitle { color: var(--muted); font-size: 0.85rem; margin-bottom: 24px; }
  .subtitle a { color: #3498db; text-decoration: none; }
  .subtitle a:hover { text-decoration: underline; }
  .game-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; margin-bottom: 16px; padding: 16px; display: grid; grid-template-columns: 120px 1fr; gap: 16px; }
  .game-card.found { border-left: 4px solid var(--green); }
  .game-card.missing { border-left: 4px solid var(--red); }
  .artwork-preview img { width: 100px; border-radius: 4px; background: #000; }
  .artwork-preview .no-img { width: 100px; height: 150px; background: #333; border-radius: 4px; display: flex; align-items: center; justify-content: center; color: var(--red); font-size: 0.75rem; }
  .game-info h2 { font-size: 1rem; margin-bottom: 4px; }
  .game-info .meta { font-size: 0.8rem; color: var(--muted); margin-bottom: 8px; }
  .game-info .meta a { color: #3498db; text-decoration: none; }
  .game-info .meta a:hover { text-decoration: underline; }
  .section { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; }
  .section h2 { font-size: 1rem; margin-bottom: 8px; }
  .kv { display: grid; grid-template-columns: 140px 1fr; gap: 4px 12px; font-size: 0.85rem; }
  .kv .label { color: var(--muted); }
  .path-table { width: 100%; border-collapse: collapse; font-size: 0.75rem; }
  .path-table th { text-align: left; color: var(--muted); padding: 4px 8px; border-bottom: 1px solid var(--border); }
  .path-table td { padding: 4px 8px; border-bottom: 1px solid rgba(255,255,255,0.05); word-break: break-all; }
  .exists { color: var(--green); font-weight: 600; }
  .missing-path { color: var(--red); }
  .preview img { max-width: 300px; border-radius: 4px; margin-top: 8px; }
  .badge { display: inline-block; font-size: 0.7rem; padding: 1px 6px; border-radius: 3px; }
  .badge-found { background: var(--green); color: #000; }
  .badge-missing { background: var(--red); color: #fff; }
  .badge-shortcut { background: #9b59b6; color: #fff; }
</style>
""";

    private static string BuildOverviewHtml(List<ArtworkDiagnostics> diagnostics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Artwork Debug — Top 20 Games</title>");
        sb.Append(CssBlock);
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>Artwork Debug — Top 20 Games</h1>");
        sb.AppendLine("<p class=\"subtitle\">Shows all artwork path resolution attempts for each game.</p>");

        if (diagnostics.Count == 0)
        {
            sb.AppendLine("<p>No games found. Is Steam installed and running?</p>");
        }

        foreach (var diag in diagnostics)
        {
            var hasArt = diag.ResolvedPath != null;
            var cardClass = hasArt ? "found" : "missing";
            var badgeClass = hasArt ? "badge-found" : "badge-missing";
            var badgeText = hasArt ? "FOUND" : "MISSING";

            sb.AppendLine($"<div class=\"game-card {cardClass}\">");
            sb.AppendLine("  <div class=\"artwork-preview\">");
            if (hasArt)
                sb.AppendLine($"    <img src=\"/api/steam/artwork/{diag.AppId}\" alt=\"{Encode(diag.GameName)}\" />");
            else
                sb.AppendLine("    <div class=\"no-img\">No image</div>");
            sb.AppendLine("  </div>");

            sb.AppendLine("  <div class=\"game-info\">");
            sb.Append($"    <h2>{Encode(diag.GameName)} <span class=\"badge {badgeClass}\">{badgeText}</span>");
            if (diag.IsShortcut)
                sb.Append(" <span class=\"badge badge-shortcut\">SHORTCUT</span>");
            sb.AppendLine("</h2>");

            sb.AppendLine("    <div class=\"meta\">");
            sb.Append($"      AppId: {diag.AppId} | FileId: {diag.FileId}");
            sb.Append($" | <a href=\"/api/steam/artwork/{diag.AppId}/debug\">Full diagnostics</a>");
            if (!string.IsNullOrEmpty(diag.CdnUrl))
                sb.Append($" | <a href=\"{diag.CdnUrl}\" target=\"_blank\">CDN fallback</a>");
            sb.AppendLine();
            sb.AppendLine("    </div>");

            sb.AppendLine("    <table class=\"path-table\">");
            sb.AppendLine("      <tr><th>Category</th><th>Path</th><th>Status</th></tr>");
            foreach (var p in diag.PathsChecked)
            {
                var cls = p.Exists ? "exists" : "missing-path";
                var status = p.Exists ? FormatSize(p.SizeBytes) : "not found";
                sb.AppendLine($"      <tr><td>{Encode(p.Category)}</td><td class=\"{cls}\">{Encode(p.Path)}</td><td class=\"{cls}\">{status}</td></tr>");
            }
            sb.AppendLine("    </table>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string BuildDetailHtml(ArtworkDiagnostics diag)
    {
        var hasArt = diag.ResolvedPath != null;
        var statusBadge = hasArt
            ? "<span class=\"badge badge-found\">FOUND</span>"
            : "<span class=\"badge badge-missing\">MISSING</span>";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Artwork Debug — {Encode(diag.GameName)}</title>");
        sb.Append(CssBlock);
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>Artwork Debug — {Encode(diag.GameName)}</h1>");
        sb.AppendLine("<p class=\"subtitle\"><a href=\"/api/steam/artwork/debug\">Back to overview</a></p>");

        // Game Info section
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Game Info</h2>");
        sb.AppendLine("  <div class=\"kv\">");
        sb.AppendLine($"    <span class=\"label\">AppId</span><span>{diag.AppId}</span>");
        sb.AppendLine($"    <span class=\"label\">FileId</span><span>{diag.FileId}</span>");
        sb.AppendLine($"    <span class=\"label\">Is Shortcut</span><span>{diag.IsShortcut}</span>");
        sb.AppendLine($"    <span class=\"label\">Status</span><span>{statusBadge}</span>");
        sb.AppendLine($"    <span class=\"label\">Resolved Path</span><span>{Encode(diag.ResolvedPath ?? "(none)")}</span>");
        if (!string.IsNullOrEmpty(diag.CdnUrl))
            sb.AppendLine($"    <span class=\"label\">CDN URL</span><span><a href=\"{diag.CdnUrl}\" target=\"_blank\" style=\"color:#3498db\">{diag.CdnUrl}</a></span>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");

        // Preview section
        if (hasArt)
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("  <h2>Preview</h2>");
            sb.AppendLine("  <div class=\"preview\">");
            sb.AppendLine($"    <img src=\"/api/steam/artwork/{diag.AppId}\" alt=\"{Encode(diag.GameName)}\" />");
            sb.AppendLine("  </div>");
            sb.AppendLine("</div>");
        }

        // Path Resolution Trail
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("  <h2>Path Resolution Trail</h2>");
        sb.AppendLine("  <table class=\"path-table\">");
        sb.AppendLine("    <tr><th>#</th><th>Category</th><th>Path</th><th>Exists</th><th>Size</th></tr>");

        var i = 1;
        foreach (var p in diag.PathsChecked)
        {
            var cls = p.Exists ? "exists" : "missing-path";
            var existsText = p.Exists ? "YES" : "no";
            var sizeText = p.Exists ? FormatSize(p.SizeBytes) : "-";
            sb.AppendLine($"    <tr><td>{i++}</td><td>{Encode(p.Category)}</td><td class=\"{cls}\">{Encode(p.Path)}</td><td class=\"{cls}\">{existsText}</td><td>{sizeText}</td></tr>");
        }

        sb.AppendLine("  </table>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);

    private static string FormatSize(long? bytes)
    {
        if (bytes == null) return "-";
        return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024} KB";
    }
}
