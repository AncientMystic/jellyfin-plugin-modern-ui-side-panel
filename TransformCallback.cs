using System;

namespace Jellyfin.Plugin.ModernSidePanel;

/// <summary>
/// In-memory index.html transform callback for Jellyfin.Plugin.FileTransformation.
/// Registered via reflection (see <see cref="Plugin.TryRegisterFileTransformation"/>)
/// so this plugin has NO hard dependency on FileTransformation — if that companion
/// plugin is installed we get non-destructive injection; otherwise we fall back
/// to the on-disk <see cref="IndexPatcher"/> patch.
///
/// SINGLE canonical overload: <see cref="Transform(string?)"/> only. A second
/// Transform(object?) overload was removed because the host resolves the callback
/// via Type.GetMethod("Transform"), which throws AmbiguousMatchException when
/// two overloads exist. JSON wrappers are unwrapped inside this one method.
/// </summary>
public static class TransformCallback
{
	/// <summary>
	/// Canonical transform entry point. Accepts raw HTML or a JSON wrapper
	/// ({contents/Content/content/html/data/fileContents} string property) and
	/// returns transformed HTML.
	/// </summary>
	public static string Transform(string? contents)
	{
		// Unwrap JSON wrapper if the input looks like JSON, then transform.
		var html = TryUnwrapJsonContents(contents) ?? contents;
		return TransformHtml(html);
	}

	private static string TransformHtml(string? html)
	{
		// Disable switch must stop the in-memory path too. Plugin.Instance may be
		// null in the transform host context (callback invoked on a different
		// load path) — treat null as enabled to preserve injection.
		if (Plugin.Instance?.Configuration.EnablePlugin == false)
		{
			return html ?? string.Empty;
		}

		if (string.IsNullOrEmpty(html))
		{
			return html ?? string.Empty;
		}

		// Already injected (e.g. disk patch also applied) — don't duplicate.
		if (html.IndexOf("ModernSidePanel BEGIN", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return html;
		}

		var snippet = IndexPatcher.BuildBlock();
		var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
		if (headEnd >= 0)
		{
			return html.Insert(headEnd, Environment.NewLine + snippet + Environment.NewLine);
		}

		return html + Environment.NewLine + snippet + Environment.NewLine;
	}

	private static string? TryUnwrapJsonContents(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		json = json.Trim();
		if (!json.StartsWith("{", StringComparison.Ordinal))
		{
			return null;
		}

		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			foreach (var key in new[] { "contents", "Contents", "content", "Content", "html", "Html", "data", "fileContents" })
			{
				if (root.TryGetProperty(key, out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					return c.GetString();
				}
			}
		}
		catch
		{
			// Not System.Text.Json — ignore, caller uses raw string.
		}

		return null;
	}
}
