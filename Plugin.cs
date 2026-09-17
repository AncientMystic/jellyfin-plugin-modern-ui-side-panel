using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ModernSidePanel;

/// <summary>
/// ModernSidePanel plugin. Keeps modern UI styling, restores persistent
/// legacy side panel, hides the top bar header via injected JS/CSS.
/// Preferred injection is in-memory via FileTransformation (if installed);
/// fallback is an on-disk index.html patch (with backup) by <see cref="IndexPatcher"/>.
/// Assets are served by <see cref="Api.SidePanelController"/>.
/// Supports legacy DOM (.mainDrawer/.skinHeader) AND modern v12 MUI shell
/// (MuiDrawer/MuiAppBar) — see Web/sidepanel.js.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;
    private readonly IApplicationPaths _paths;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _paths = applicationPaths;
        _logger = logger;
        Instance = this;

        ConfigurationChanged += (_, _) =>
        {
            _logger.LogInformation("[ModernSidePanel] Configuration changed, re-applying index.html patch.");
            TryPatchIndexHtml();
        };

        // Preferred: in-memory injection (no disk modification). Fallback: disk patch.
        TryRegisterFileTransformation();
        // Patch on startup + retries (web folder may mount late in docker;
        // FileTransformation may also load after us, so retry its registration too).
        TryPatchIndexHtml();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(3 * (i + 1))).ConfigureAwait(false);
                TryRegisterFileTransformation();
                TryPatchIndexHtml();
            }
        });
    }

    // Volatile backing field so the delayed retry-loop thread always observes the
    // fully-constructed instance published by the plugin-loader thread.
    private static volatile Plugin? _instance;

    public static Plugin? Instance
    {
        get => _instance;
        private set => _instance = value;
    }

    public override string Name => "Modern UI Side Panel";

    public override Guid Id => Guid.Parse("9f7a2ad0-5e4e-4b2c-9c3a-1d2f6e7a8b9c1");

    public override string Description => "Keeps the modern Jellyfin UI style, restores a persistent legacy left side panel, and hides the top bar header.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }

    public void TryPatchIndexHtml()
    {
        try
        {
            var root = DetectWebRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                _logger.LogWarning("[ModernSidePanel] Web root not found; skipping index.html patch. Client injection will not load until web root is available.");
                return;
            }

            var ok = IndexPatcher.EnsurePatched(_logger, root, Configuration.EnablePlugin);
            _logger.LogInformation("[ModernSidePanel] Patch result: {Result} (webRoot={Root})", ok, root);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ModernSidePanel] TryPatchIndexHtml failed");
        }
    }

    /// <summary>
    /// Registers an in-memory index.html transform with the FileTransformation
    /// companion plugin (if installed). Reflection-only: no hard reference, so
    /// this plugin loads fine with or without it. See TransformCallback.
    /// </summary>
    public void TryRegisterFileTransformation()
    {
        try
        {
            var ftAssembly = System.Runtime.Loader.AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") == true);

            if (ftAssembly is null)
            {
                _logger.LogInformation("[ModernSidePanel] FileTransformation not found; using index.html disk patch.");
                return;
            }

            var iface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var method = iface?.GetMethod("RegisterTransformation");
            if (method is null)
            {
                _logger.LogWarning("[ModernSidePanel] FileTransformation found but RegisterTransformation missing; using disk patch.");
                return;
            }

            var payload = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = Id.ToString(),
                ["fileNamePattern"] = "index\\.html",
                ["callbackAssembly"] = GetType().Assembly.FullName,
                ["callbackClass"] = typeof(TransformCallback).FullName,
                ["callbackMethod"] = nameof(TransformCallback.Transform)
            };

            // "Transform" is now unambiguous: TransformCallback exposes a SINGLE
            // canonical Transform(string?) overload (object? overload deleted).
            var result = method.Invoke(null, new object?[] { payload.ToJsonString() });
            _logger.LogInformation("[ModernSidePanel] Registered in-memory index.html transform via FileTransformation. Result: {Result}", result ?? "<null>");
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            _logger.LogWarning(ex.InnerException ?? ex, "[ModernSidePanel] FileTransformation registration failed ({Inner}); falling back to disk patch.", (ex.InnerException ?? ex).Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ModernSidePanel] FileTransformation registration failed ({Message}); falling back to disk patch.", ex.Message);
        }
    }

    private string? DetectWebRoot()
    {
        // 1. Jellyfin-provided web path (most reliable).
        try
        {
            var webPath = _paths.WebPath;
            if (!string.IsNullOrWhiteSpace(webPath)
                && System.IO.Directory.Exists(webPath)
                && System.IO.File.Exists(System.IO.Path.Combine(webPath, "index.html")))
            {
                return webPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ModernSidePanel] ApplicationPaths.WebPath probe failed");
        }

        // 2. Well-known docker / bare-metal / Windows locations.
        var candidates = new[]
        {
            "/usr/share/jellyfin/web",
            "/usr/lib/jellyfin/bin/jellyfin-web",
            "/var/lib/jellyfin/web",
            "/opt/jellyfin/web",
            "/jellyfin/web",
            System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData), "Jellyfin", "Server", "web"),
            System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Jellyfin", "Server", "jellyfin-web"),
            System.IO.Path.Combine(System.Environment.CurrentDirectory, "web"),
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "web")
        };

        foreach (var p in candidates)
        {
            try
            {
                if (System.IO.Directory.Exists(p)
                    && System.IO.File.Exists(System.IO.Path.Combine(p, "index.html")))
                {
                    return p;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
