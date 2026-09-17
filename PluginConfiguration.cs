using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ModernSidePanel;

/// <summary>
/// User-configurable options (Dashboard -> Plugins -> Modern UI Side Panel).
/// These are exposed to the web client via /Plugins/ModernSidePanel/config.js
/// and applied as CSS variables + JS flags.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Master switch. When false, index.html patch is removed and nothing is injected.
    /// </summary>
    public bool EnablePlugin { get; set; } = true;

    /// <summary>
    /// Hide the top bar header (.skinHeader / .headerTop). Default true.
    /// </summary>
    public bool HideTopBar { get; set; } = true;

    /// <summary>
    /// Keep drawer open as a persistent sidebar on desktop. Default true.
    /// </summary>
    public bool PersistentSidebar { get; set; } = true;

    /// <summary>
    /// Sidebar width in px (desktop). Default 272.
    /// </summary>
    public int SidebarWidth { get; set; } = 272;

    /// <summary>
    /// Show Jellyfin logo at the top of the sidebar. Default true.
    /// </summary>
    public bool ShowLogo { get; set; } = true;

    /// <summary>
    /// Move search + user buttons into the sidebar so they stay reachable
    /// when the top bar is hidden. Default true.
    /// </summary>
    public bool RelocateHeaderButtons { get; set; } = true;

    /// <summary>
    /// Collapse to overlay drawer below this viewport width (px). Default 768.
    /// Below this width the top bar is restored and the sidebar behaves like the stock drawer.
    /// </summary>
    public int MobileBreakpoint { get; set; } = 768;

    /// <summary>
    /// Backdrop blur intensity for the sidebar (px). Matches modern UI glassmorphism. Default 18.
    /// </summary>
    public int BlurStrength { get; set; } = 18;
}
