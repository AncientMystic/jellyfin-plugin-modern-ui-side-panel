using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ModernSidePanel.Api;

/// <summary>
/// Serves the injected assets + dynamic config.
/// Routes: /Plugins/ModernSidePanel/{sidepanel.js, sidepanel.css, config.js}
/// </summary>
[ApiController]
[Route("Plugins/ModernSidePanel")]
public class SidePanelController : ControllerBase
{
    private const string JsResource = "Jellyfin.Plugin.ModernSidePanel.Web.sidepanel.js";
    private const string CssResource = "Jellyfin.Plugin.ModernSidePanel.Web.sidepanel.css";

    // Assets load pre-login (index.html references them before auth); payload is
    // non-sensitive (static JS/CSS + boolean/numeric layout flags), so anonymous is safe.
    [HttpGet("sidepanel.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public IActionResult GetJs()
    {
        var js = ReadEmbedded(JsResource);
        if (js is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=3600";
        return Content(js, "application/javascript; charset=utf-8");
    }

    [HttpGet("sidepanel.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    public IActionResult GetCss()
    {
        var css = ReadEmbedded(CssResource);
        if (css is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=3600";
        return Content(css, "text/css; charset=utf-8");
    }

    /// <summary>
    /// Dynamic per-server config. Loaded BEFORE sidepanel.js so the client
    /// can read window.ModernSidePanelConfig on boot.
    /// </summary>
    [HttpGet("config.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public IActionResult GetConfig()
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            enablePlugin = cfg.EnablePlugin,
            hideTopBar = cfg.HideTopBar,
            persistentSidebar = cfg.PersistentSidebar,
            sidebarWidth = cfg.SidebarWidth,
            showLogo = cfg.ShowLogo,
            relocateHeaderButtons = cfg.RelocateHeaderButtons,
            mobileBreakpoint = cfg.MobileBreakpoint,
            blurStrength = cfg.BlurStrength
        });

        // Future-proof XSS: break any "</script>" sequence inside the JSON payload.
        json = json.Replace("</", "<\\/", System.StringComparison.Ordinal);
        var js = $"window.ModernSidePanelConfig = {json};";
        Response.Headers["Cache-Control"] = "no-cache";
        return Content(js, "application/javascript; charset=utf-8");
    }

    private static string? ReadEmbedded(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
