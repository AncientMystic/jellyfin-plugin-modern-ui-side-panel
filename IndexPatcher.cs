using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ModernSidePanel;

/// <summary>
/// Patches jellyfin-web index.html to load the plugin's JS/CSS.
/// Idempotent: uses BEGIN/END markers, refreshes stale blocks, keeps a .modernsidepanel.bak backup.
///
/// NOTE: the .bak backup lives inside the webroot (web-reachable) for compat with
/// prior versions; it is low-sensitivity (a copy of stock index.html +/− our marker
/// block). .tmp files are always deleted in a finally block.
/// </summary>
public static class IndexPatcher
{
	public const string BeginMark = "<!-- ModernSidePanel BEGIN -->";
	public const string EndMark = "<!-- ModernSidePanel END -->";

	private static readonly object _patchLock = new();

	public static string BuildBlock()
	{
		var v = typeof(IndexPatcher).Assembly.GetName().Version?.ToString() ?? "1.0.0";
		var sb = new StringBuilder();
		sb.AppendLine(BeginMark);
		sb.AppendLine($"<link rel=\"stylesheet\" href=\"/Plugins/ModernSidePanel/sidepanel.css?v={v}\">");
		sb.AppendLine($"<script src=\"/Plugins/ModernSidePanel/config.js?v={v}\"></script>");
		sb.AppendLine($"<script src=\"/Plugins/ModernSidePanel/sidepanel.js?v={v}\" defer></script>");
		sb.AppendLine(EndMark);
		return sb.ToString();
	}

	private static string Norm(string s) => s.Replace("\r\n", "\n").Trim();

	public static bool EnsurePatched(ILogger logger, string webRootPath, bool enabled = true)
	{
		lock (_patchLock)
		{
			try
			{
				var indexPath = Path.Combine(webRootPath, "index.html");
				if (IsSymlink(indexPath, logger))
				{
					logger.LogWarning("[ModernSidePanel] index.html is a symlink; refusing to patch {Path}", indexPath);
					return false;
				}

				if (!File.Exists(indexPath))
				{
					logger.LogWarning("[ModernSidePanel] index.html not found at {Path}", indexPath);
					return false;
				}

				if (!enabled)
				{
					return EnsureUnpatchedLocked(logger, webRootPath);
				}

				if (!IsWritable(indexPath, logger))
				{
					return false;
				}

				var html = File.ReadAllText(indexPath, Encoding.UTF8);
				var block = BuildBlock();
				var (start, end) = FindInjectRange(html);

				if (start >= 0 && end >= 0)
				{
					var current = Norm(html.Substring(start, end - start));
					if (string.Equals(current, Norm(block), StringComparison.Ordinal))
					{
						logger.LogInformation("[ModernSidePanel] index.html patch already up to date");
						return true;
					}

					html = html.Remove(start, end - start).Insert(start, block);
					logger.LogInformation("[ModernSidePanel] Existing inject block refreshed");
				}
				else
				{
					var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
					if (headEnd >= 0)
					{
						html = html.Insert(headEnd, Environment.NewLine + block + Environment.NewLine);
					}
					else
					{
						var bodyEnd = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
						if (bodyEnd >= 0)
						{
							html = html.Insert(bodyEnd, Environment.NewLine + block + Environment.NewLine);
						}
						else
						{
							html += Environment.NewLine + block + Environment.NewLine;
						}
					}
				}

				EnsureBackup(indexPath, logger);
				AtomicWrite(indexPath, html);

				var verify = File.ReadAllText(indexPath, Encoding.UTF8);
				if (verify.Contains(BeginMark, StringComparison.OrdinalIgnoreCase)
					&& verify.Contains(EndMark, StringComparison.OrdinalIgnoreCase))
				{
					WriteCompressedCopiesIfPresent(logger, webRootPath, verify);
					return true;
				}

				logger.LogError("[ModernSidePanel] Patch verification FAILED");
				return false;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "[ModernSidePanel] Failed to patch index.html");
				return false;
			}
		}
	}

	public static bool EnsureUnpatched(ILogger logger, string webRootPath)
	{
		lock (_patchLock)
		{
			return EnsureUnpatchedLocked(logger, webRootPath);
		}
	}

	private static bool EnsureUnpatchedLocked(ILogger logger, string webRootPath)
	{
		try
		{
			var indexPath = Path.Combine(webRootPath, "index.html");
			if (IsSymlink(indexPath, logger))
			{
				logger.LogWarning("[ModernSidePanel] index.html is a symlink; refusing to unpatch {Path}", indexPath);
				return false;
			}

			if (!File.Exists(indexPath))
			{
				return true;
			}

			if (!IsWritable(indexPath, logger))
			{
				return false;
			}

			var backupPath = indexPath + ".modernsidepanel.bak";
			var html = File.ReadAllText(indexPath, Encoding.UTF8);
			var hasMarker = html.Contains(BeginMark, StringComparison.OrdinalIgnoreCase)
				|| html.Contains(EndMark, StringComparison.OrdinalIgnoreCase);

			// Prefer inline strip always: safe against server upgrades that changed
			// index.html after the backup was taken. Only restore .bak when the
			// current file has NO marker AND the backup has no marker AND the backup
			// length is within 50% of current length (sanity: avoids downgrading a
			// newer server build with a stale backup).
			if (!hasMarker && File.Exists(backupPath))
			{
				try
				{
					var backupHtml = File.ReadAllText(backupPath, Encoding.UTF8);
					var backupHasMarker = backupHtml.Contains(BeginMark, StringComparison.OrdinalIgnoreCase)
						|| backupHtml.Contains(EndMark, StringComparison.OrdinalIgnoreCase);
					var lengthOk = backupHtml.Length > 0
						&& Math.Abs(backupHtml.Length - html.Length) <= html.Length * 0.5;
					if (!backupHasMarker && lengthOk)
					{
						AtomicWrite(indexPath, backupHtml);
						logger.LogInformation("[ModernSidePanel] Restored index.html from backup");
					}
					else
					{
						logger.LogInformation("[ModernSidePanel] Backup stale (marker={Marker}, lengthOk={LengthOk}); keeping current file and deleting stale backup", backupHasMarker, lengthOk);
						try { File.Delete(backupPath); } catch { /* ignore */ }
					}

					WriteCompressedCopiesIfPresent(logger, webRootPath, File.ReadAllText(indexPath, Encoding.UTF8));
					return true;
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "[ModernSidePanel] Backup restore failed, falling back to inline removal");
				}
			}

			var (start, end) = FindInjectRange(html);
			if (start < 0 || end < 0)
			{
				// No marker: delete stale .bak so it can't downgrade a future unpatch.
				if (File.Exists(backupPath))
				{
					try { File.Delete(backupPath); } catch { /* ignore */ }
				}

				return true;
			}

			html = html.Remove(start, end - start);
			AtomicWrite(indexPath, html);
			WriteCompressedCopiesIfPresent(logger, webRootPath, html);
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "[ModernSidePanel] EnsureUnpatched failed");
			return false;
		}
	}

	private static (int start, int end) FindInjectRange(string html)
	{
		var begin = html.IndexOf(BeginMark, StringComparison.OrdinalIgnoreCase);
		if (begin < 0)
		{
			return (-1, -1);
		}

		var end = html.IndexOf(EndMark, begin, StringComparison.OrdinalIgnoreCase);
		if (end < 0)
		{
			return (-1, -1);
		}

		end += EndMark.Length;
		return (begin, end);
	}

	private static bool IsSymlink(string path, ILogger logger)
	{
		try
		{
			var full = Path.GetFullPath(path);
			if (!File.Exists(full) && !Directory.Exists(full))
			{
				return false;
			}

			return (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "[ModernSidePanel] Symlink probe failed for {Path}", path);
			return false;
		}
	}

	private static bool IsWritable(string path, ILogger logger)
	{
		try
		{
			using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
			return true;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "[ModernSidePanel] index.html is not writable: {Path}", path);
			return false;
		}
	}

	private static void AtomicWrite(string indexPath, string html)
	{
		var tmp = indexPath + ".modernsidepanel.tmp";
		try
		{
			File.WriteAllText(tmp, html, Encoding.UTF8);
			File.Move(tmp, indexPath, overwrite: true);
		}
		finally
		{
			try
			{
				if (File.Exists(tmp))
				{
					File.Delete(tmp);
				}
			}
			catch
			{
				// ignore cleanup failure
			}
		}
	}

	private static void EnsureBackup(string indexPath, ILogger logger)
	{
		try
		{
			var backupPath = indexPath + ".modernsidepanel.bak";
			if (!File.Exists(backupPath))
			{
				File.Copy(indexPath, backupPath);
				logger.LogInformation("[ModernSidePanel] Created backup: {Backup}", backupPath);
			}
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "[ModernSidePanel] Could not create backup");
		}
	}

	/// <summary>
	/// Some deployments ship pre-compressed index.html.gz/.br copies. The Jellyfin
	/// server does NOT regenerate them after we modify index.html, so deleting them
	/// makes clients fall back to the uncompressed file (costs extra bandwidth but
	/// guarantees the patched markup is served regardless of Accept-Encoding).
	/// </summary>
	private static void WriteCompressedCopiesIfPresent(ILogger logger, string webRoot, string html)
	{
		foreach (var ext in new[] { ".gz", ".br" })
		{
			try
			{
				var p = Path.Combine(webRoot, "index.html" + ext);
				if (File.Exists(p))
				{
					File.Delete(p);
					logger.LogInformation("[ModernSidePanel] Deleted stale pre-compressed {Ext} (server does not regenerate; clients fall back to uncompressed index.html)", ext);
				}
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "[ModernSidePanel] Could not clean {Ext} copy", ext);
			}
		}
	}
}
