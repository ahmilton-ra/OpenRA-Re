#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Primitives;

namespace OpenRA.FileSystem
{
	public interface IReadOnlyFileSystem
	{
		Stream Open(string filename);
		bool TryGetPackageContaining(string path, out IReadOnlyPackage package, out string filename);
		bool TryOpen(string filename, out Stream s);
		bool Exists(string filename);
		bool IsExternalFile(string filename);
	}

	public class FileSystem : IReadOnlyFileSystem
	{
		public IEnumerable<IReadOnlyPackage> MountedPackages => mountedPackages.Keys;
		readonly Dictionary<IReadOnlyPackage, int> mountedPackages = [];
		readonly Dictionary<string, IReadOnlyPackage> explicitMounts = [];
		readonly string modID;

		// Mod packages that should not be disposed
		readonly List<IReadOnlyPackage> modPackages = [];
		readonly IReadOnlyDictionary<string, Manifest> installedMods;
		readonly IPackageLoader[] packageLoaders;

		Cache<string, List<IReadOnlyPackage>> fileIndex = new(_ => []);

		public FileSystem(string modID, IReadOnlyDictionary<string, Manifest> installedMods, IPackageLoader[] packageLoaders)
		{
			this.modID = modID;
			this.installedMods = installedMods;
			this.packageLoaders = packageLoaders
				.Append(new ZipFileLoader())
				.ToArray();
		}

		public bool TryParsePackage(Stream stream, string filename, out IReadOnlyPackage package)
		{
			package = null;
			foreach (var packageLoader in packageLoaders)
				if (packageLoader.TryParsePackage(stream, filename, this, out package))
					return true;

			return false;
		}

		public IReadOnlyPackage OpenPackage(string filename)
		{
			// Raw directories are the easiest and one of the most common cases, so try these first
			var resolvedPath = Platform.ResolvePath(filename);
			if (!resolvedPath.Contains('|') && Directory.Exists(resolvedPath))
				return new Folder(resolvedPath);

			// Children of another package require special handling
			if (TryGetPackageContaining(filename, out var parent, out var subPath))
				return parent.OpenPackage(subPath, this);

			// Try and open it normally
			var stream = Open(filename);
			if (TryParsePackage(stream, filename, out var package))
				return package;

			// No package loaders took ownership of the stream, so clean it up
			stream.Dispose();

			return null;
		}

		public void Mount(string name, string explicitName = null)
		{
			var optional = name.StartsWith('~');
			if (optional)
				name = name[1..];

			try
			{
				IReadOnlyPackage package;
				if (name.StartsWith('$'))
				{
					name = name[1..];

					if (!installedMods.TryGetValue(name, out var mod))
						throw new InvalidOperationException($"Could not load mod '{name}'. Available mods: {installedMods.Keys.JoinWith(", ")}");

					package = mod.Package;
					modPackages.Add(package);
				}
				else
				{
					package = OpenPackage(name);
					if (package == null)
						throw new InvalidOperationException($"Could not open package '{name}', file not found or its format is not supported.");
				}

				Mount(package, explicitName);
			}
			catch when (optional)
			{
			}
		}

		public void Mount(IReadOnlyPackage package, string explicitName = null)
		{
			if (mountedPackages.TryGetValue(package, out var mountCount))
			{
				// Package is already mounted
				// Increment the mount count and bump up the file loading priority
				mountedPackages[package] = mountCount + 1;
				foreach (var filename in package.Contents)
				{
					fileIndex[filename].Remove(package);
					fileIndex[filename].Add(package);
				}
			}
			else
			{
				// Mounting the package for the first time
				mountedPackages.Add(package, 1);

				if (explicitName != null)
					explicitMounts.Add(explicitName, package);

				foreach (var filename in package.Contents)
					fileIndex[filename].Add(package);
			}
		}

		public bool Unmount(IReadOnlyPackage package)
		{
			if (!mountedPackages.TryGetValue(package, out var mountCount))
				return false;

			if (--mountCount <= 0)
			{
				foreach (var packagesForFile in fileIndex.Values)
					packagesForFile.RemoveAll(p => p == package);

				mountedPackages.Remove(package);
				var explicitKeys = explicitMounts.Where(kv => kv.Value == package)
					.Select(kv => kv.Key)
					.ToList();

				foreach (var key in explicitKeys)
					explicitMounts.Remove(key);

				// Mod packages aren't owned by us, so we shouldn't dispose them
				if (!modPackages.Remove(package))
					package.Dispose();
			}
			else
				mountedPackages[package] = mountCount;

			return true;
		}

		public void UnmountAll()
		{
			foreach (var package in mountedPackages.Keys)
				if (!modPackages.Contains(package))
					package.Dispose();

			mountedPackages.Clear();
			explicitMounts.Clear();
			modPackages.Clear();

			fileIndex = new Cache<string, List<IReadOnlyPackage>>(_ => []);
		}

		public void TrimExcess()
		{
			mountedPackages.TrimExcess();
			explicitMounts.TrimExcess();
			modPackages.TrimExcess();
			foreach (var packages in fileIndex.Values)
				packages.TrimExcess();
		}

		Stream GetFromCache(string filename)
		{
			var package = fileIndex[filename]
				.LastOrDefault(x => x.Contains(filename));

			return package?.GetStream(filename);
		}

		public Stream Open(string filename)
		{
			if (!TryOpen(filename, out var s))
				throw new FileNotFoundException($"File not found: {filename}", filename);

			return s;
		}

		public bool TryGetPackageContaining(string path, out IReadOnlyPackage package, out string filename)
		{
			var explicitSplit = path.IndexOf('|');
			if (explicitSplit > 0 && explicitMounts.TryGetValue(path[..explicitSplit], out package))
			{
				filename = path[(explicitSplit + 1)..];
				return true;
			}

			package = fileIndex[path].LastOrDefault(x => x.Contains(path));
			filename = path;

			return package != null;
		}

		public bool TryOpen(string filename, out Stream s)
		{
			var explicitSplit = filename.IndexOf('|');
			if (explicitSplit > 0 && explicitMounts.TryGetValue(filename[..explicitSplit], out var explicitPackage))
			{
				s = explicitPackage.GetStream(filename[(explicitSplit + 1)..]);
				if (s != null)
					return true;
			}

			s = GetFromCache(filename);
			if (s != null)
				return true;

			// The file should be in an explicit package (but we couldn't find it)
			// Thus don't try to find it using the filename (which contains the invalid '|' char)
			// This can be removed once the TODO below is resolved
			if (explicitSplit > 0)
				return false;

			// Ask each package individually
			// TODO: This fallback can be removed once the filesystem cleanups are complete
			var package = mountedPackages.Keys.LastOrDefault(x => x.Contains(filename));
			if (package != null)
			{
				s = package.GetStream(filename);
				return s != null;
			}

			s = null;
			return false;
		}

		public bool Exists(string filename)
		{
			var explicitSplit = filename.IndexOf('|');
			if (explicitSplit > 0 &&
				explicitMounts.TryGetValue(filename[..explicitSplit], out var explicitPackage) &&
				explicitPackage.Contains(filename[(explicitSplit + 1)..]))
				return true;

			return fileIndex.ContainsKey(filename);
		}

		/// <summary>
		/// Returns true if the given filename references any file outside the mod mount.
		/// </summary>
		public bool IsExternalFile(string filename)
		{
			return !filename.StartsWith($"{modID}|", StringComparison.Ordinal);
		}

		public static string ResolveCaseInsensitivePath(string path)
		{
			var resolved = Path.GetPathRoot(path);

			if (resolved == null)
				return null;

			foreach (var name in path[resolved.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			{
				// Filter out paths of the form /foo/bar/./baz
				if (name == ".")
					continue;

				resolved = Directory.GetFileSystemEntries(resolved)
					.FirstOrDefault(e => e.Equals(Path.Combine(resolved, name), StringComparison.InvariantCultureIgnoreCase));

				if (resolved == null)
					return null;
			}

			return resolved;
		}

		public string GetPrefix(IReadOnlyPackage package)
		{
			return explicitMounts.ContainsValue(package) ? explicitMounts.First(f => f.Value == package).Key : null;
		}
	}
}
