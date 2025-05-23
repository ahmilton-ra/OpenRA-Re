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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA
{
	public static class MiniYamlExts
	{
		public static void WriteToFile(this IEnumerable<MiniYamlNode> y, string filename)
		{
			File.WriteAllLines(filename, y.ToLines().Select(x => x.TrimEnd()).ToArray());
		}

		public static string WriteToString(this IEnumerable<MiniYamlNode> y)
		{
			// Remove all trailing newlines and restore the final EOF newline
			return y.ToLines().JoinWith("\n").TrimEnd('\n') + "\n";
		}

		public static IEnumerable<string> ToLines(this IEnumerable<MiniYamlNode> y)
		{
			foreach (var kv in y)
				foreach (var line in kv.Value.ToLines(kv.Key, kv.Comment))
					yield return line;
		}

		public static void WriteToFile(this IEnumerable<MiniYamlNodeBuilder> y, string filename)
		{
			File.WriteAllLines(filename, y.ToLines().Select(x => x.TrimEnd()).ToArray());
		}

		public static string WriteToString(this IEnumerable<MiniYamlNodeBuilder> y)
		{
			// Remove all trailing newlines and restore the final EOF newline
			return y.ToLines().JoinWith("\n").TrimEnd('\n') + "\n";
		}

		public static IEnumerable<string> ToLines(this IEnumerable<MiniYamlNodeBuilder> y)
		{
			foreach (var kv in y)
				foreach (var line in kv.Value.ToLines(kv.Key, kv.Comment))
					yield return line;
		}
	}

	public sealed class MiniYamlNode
	{
		public readonly struct SourceLocation(string name, int line)
		{
			public readonly string Name = name;
			public readonly int Line = line;

			public override string ToString() { return $"{Name}:{Line}"; }
		}

		public readonly SourceLocation Location;
		public readonly string Key;
		public readonly MiniYaml Value;
		public readonly string Comment;

		public MiniYamlNode WithValue(MiniYaml value)
		{
			if (Value == value)
				return this;
			return new MiniYamlNode(Key, value, Comment, Location);
		}

		public MiniYamlNode(string k, MiniYaml v, string c = null)
		{
			Key = k;
			Value = v;
			Comment = c;
		}

		public MiniYamlNode(string k, MiniYaml v, string c, SourceLocation loc)
			: this(k, v, c)
		{
			Location = loc;
		}

		public MiniYamlNode(string k, string v, string c = null)
			: this(k, new MiniYaml(v, []), c) { }

		public MiniYamlNode(string k, string v, IEnumerable<MiniYamlNode> n)
			: this(k, new MiniYaml(v, n), null) { }

		public override string ToString()
		{
			return $"{{YamlNode: {Key} @ {Location}}}";
		}
	}

	public sealed class MiniYaml
	{
		const int SpacesPerLevel = 4;
		static readonly Func<string, string> StringIdentity = s => s;
		static readonly Func<MiniYaml, MiniYaml> MiniYamlIdentity = my => my;
		static readonly Dictionary<string, MiniYamlNode> ConflictScratch = [];

		public readonly string Value;
		public readonly ImmutableArray<MiniYamlNode> Nodes;

		public MiniYaml WithValue(string value)
		{
			if (Value == value)
				return this;
			return new MiniYaml(value, Nodes);
		}

		public MiniYaml WithNodes(IEnumerable<MiniYamlNode> nodes)
		{
			if (nodes is ImmutableArray<MiniYamlNode> n && Nodes == n)
				return this;
			return new MiniYaml(Value, nodes);
		}

		public MiniYaml WithNodesAppended(IEnumerable<MiniYamlNode> nodes)
		{
			var newNodes = Nodes.AddRange(nodes);
			if (Nodes == newNodes)
				return this;
			return new MiniYaml(Value, newNodes);
		}

		public MiniYamlNode NodeWithKey(string key)
		{
			var result = NodeWithKeyOrDefault(key);
			if (result == null)
				throw new InvalidDataException($"No node with key '{key}'");
			return result;
		}

		public MiniYamlNode NodeWithKeyOrDefault(string key)
		{
			// PERF: Avoid LINQ.
			var first = true;
			MiniYamlNode result = null;
			foreach (var node in Nodes)
			{
				if (node.Key != key)
					continue;

				if (!first)
					throw new InvalidDataException($"Duplicate key '{node.Key}' in {node.Location}");

				first = false;
				result = node;
			}

			return result;
		}

		public Dictionary<string, MiniYaml> ToDictionary()
		{
			return ToDictionary(MiniYamlIdentity);
		}

		public Dictionary<string, TElement> ToDictionary<TElement>(Func<MiniYaml, TElement> elementSelector)
		{
			return ToDictionary(StringIdentity, elementSelector);
		}

		public Dictionary<TKey, TElement> ToDictionary<TKey, TElement>(
			Func<string, TKey> keySelector, Func<MiniYaml, TElement> elementSelector)
		{
			var ret = new Dictionary<TKey, TElement>(Nodes.Length);
			foreach (var y in Nodes)
			{
				var key = keySelector(y.Key);
				var element = elementSelector(y.Value);
				if (!ret.TryAdd(key, element))
					throw new InvalidDataException($"Duplicate key '{y.Key}' in {y.Location}");
			}

			return ret;
		}

		public MiniYaml(string value)
			: this(value, []) { }

		public MiniYaml(string value, IEnumerable<MiniYamlNode> nodes)
		{
			Value = value;
			Nodes = nodes.ToImmutableArray();
		}

		static List<MiniYamlNode> FromLines(IEnumerable<ReadOnlyMemory<char>> lines, string name, bool discardCommentsAndWhitespace, HashSet<string> stringPool)
		{
			// YAML config often contains repeated strings for key, values, comments.
			// Pool these strings so we only need one copy of each unique string.
			// This saves on long-term memory usage as parsed values can often live a long time.
			// A caller can also provide a pool as input, allowing de-duplication across multiple parses.
			stringPool ??= [];

			var result = new List<List<MiniYamlNode>>
			{
				new()
			};
			var parsedLines = new List<(int Level, string Key, string Value, string Comment, MiniYamlNode.SourceLocation Location)>();

			var lineNo = 0;
			foreach (var ll in lines)
			{
				var line = ll.Span;
				++lineNo;

				var keyStart = 0;
				var level = 0;
				var spaces = 0;
				var textStart = false;

				ReadOnlySpan<char> key = default;
				ReadOnlySpan<char> value = default;
				ReadOnlySpan<char> comment = default;
				var location = new MiniYamlNode.SourceLocation(name, lineNo);

				if (line.Length > 0)
				{
					var currChar = line[keyStart];

					while (!(currChar == '\n' || currChar == '\r') && keyStart < line.Length && !textStart)
					{
						currChar = line[keyStart];
						switch (currChar)
						{
							case ' ':
								spaces++;
								if (spaces >= SpacesPerLevel)
								{
									spaces = 0;
									level++;
								}

								keyStart++;
								break;
							case '\t':
								level++;
								keyStart++;
								break;
							default:
								textStart = true;
								break;
						}
					}

					// Extract key, value, comment from line as `<key>: <value>#<comment>`
					// The # character is allowed in the value if escaped (\#).
					// Leading and trailing whitespace is always trimmed from keys.
					// Leading and trailing whitespace is trimmed from values unless they
					// are marked with leading or trailing backslashes
					var keyLength = line.Length - keyStart;
					var valueStart = -1;
					var valueLength = 0;
					var commentStart = -1;
					for (var i = 0; i < line.Length; i++)
					{
						if (valueStart < 0 && line[i] == ':')
						{
							valueStart = i + 1;
							keyLength = i - keyStart;
							valueLength = line.Length - i - 1;
						}

						if (commentStart < 0 && line[i] == '#' && (i == 0 || line[i - 1] != '\\'))
						{
							commentStart = i + 1;
							if (i <= keyStart + keyLength)
								keyLength = i - keyStart;
							else
								valueLength = i - valueStart;

							break;
						}
					}

					if (keyLength > 0)
						key = line.Slice(keyStart, keyLength).Trim();

					if (valueStart >= 0)
					{
						var trimmed = line.Slice(valueStart, valueLength).Trim();
						if (trimmed.Length > 0)
							value = trimmed;
					}

					if (commentStart >= 0 && !discardCommentsAndWhitespace)
						comment = line[commentStart..];

					if (value.Length > 1)
					{
						// Remove leading/trailing whitespace guards
						var trimLeading = value[0] == '\\' && (value[1] == ' ' || value[1] == '\t') ? 1 : 0;
						var trimTrailing = value[^1] == '\\' && (value[^2] == ' ' || value[^2] == '\t') ? 1 : 0;
						if (trimLeading + trimTrailing > 0)
							value = value.Slice(trimLeading, value.Length - trimLeading - trimTrailing);

						// Remove escape characters from #
						if (value.Contains("\\#", StringComparison.Ordinal))
							value = value.ToString().Replace("\\#", "#");
					}
				}

				if (!key.IsEmpty || !discardCommentsAndWhitespace)
				{
					if (parsedLines.Count > 0 && parsedLines[^1].Level < level - 1)
						throw new YamlException($"Bad indent in miniyaml at {location}");

					while (parsedLines.Count > 0 && parsedLines[^1].Level > level)
						BuildCompletedSubNode(level);

					var keyString = key.IsEmpty ? null : key.ToString();
					var valueString = value.IsEmpty ? null : value.ToString();

					// Note: We need to support empty comments here to ensure that empty comments
					// (i.e. a lone # at the end of a line) can be correctly re-serialized
					var commentString = comment == ReadOnlySpan<char>.Empty ? null : comment.ToString();

					keyString = keyString == null ? null : stringPool.GetOrAdd(keyString);
					valueString = valueString == null ? null : stringPool.GetOrAdd(valueString);
					commentString = commentString == null ? null : stringPool.GetOrAdd(commentString);

					parsedLines.Add((level, keyString, valueString, commentString, location));
				}
			}

			if (parsedLines.Count > 0)
				BuildCompletedSubNode(0);

			return result[0];

			void BuildCompletedSubNode(int level)
			{
				var lastLevel = parsedLines[^1].Level;
				while (lastLevel >= result.Count)
					result.Add([]);

				while (parsedLines.Count > 0 && parsedLines[^1].Level >= level)
				{
					var parent = parsedLines[^1];
					var startOfRange = parsedLines.Count - 1;
					while (startOfRange > 0 && parsedLines[startOfRange - 1].Level == parent.Level)
						startOfRange--;

					for (var i = startOfRange; i < parsedLines.Count - 1; i++)
					{
						var sibling = parsedLines[i];
						result[parent.Level].Add(
							new MiniYamlNode(sibling.Key, new MiniYaml(sibling.Value), sibling.Comment, sibling.Location));
					}

					var childNodes = parent.Level + 1 < result.Count ? result[parent.Level + 1] : null;
					result[parent.Level].Add(new MiniYamlNode(
						parent.Key,
						new MiniYaml(parent.Value, childNodes ?? Enumerable.Empty<MiniYamlNode>()),
						parent.Comment,
						parent.Location));
					childNodes?.Clear();

					parsedLines.RemoveRange(startOfRange, parsedLines.Count - startOfRange);
				}
			}
		}

		public static List<MiniYamlNode> FromFile(string path, bool discardCommentsAndWhitespace = true, HashSet<string> stringPool = null)
		{
			return FromStream(File.OpenRead(path), path, discardCommentsAndWhitespace, stringPool);
		}

		public static List<MiniYamlNode> FromStream(Stream s, string name, bool discardCommentsAndWhitespace = true, HashSet<string> stringPool = null)
		{
			return FromLines(s.ReadAllLinesAsMemory(), name, discardCommentsAndWhitespace, stringPool);
		}

		public static List<MiniYamlNode> FromString(string text, string name, bool discardCommentsAndWhitespace = true, HashSet<string> stringPool = null)
		{
			return FromLines(text.Split(["\r\n", "\n"], StringSplitOptions.None).Select(s => s.AsMemory()), name, discardCommentsAndWhitespace, stringPool);
		}

		public static List<MiniYamlNode> Merge(IEnumerable<IReadOnlyCollection<MiniYamlNode>> sources)
		{
			var sourcesList = sources.ToList();
			if (sourcesList.Count == 0)
				return [];

			var tree = sourcesList
				.Where(s => s != null)
				.Select(MergeSelfPartial)
				.Aggregate(MergePartial)
				.Where(n => n.Key != null)
				.ToDictionary(n => n.Key, n => n.Value);

			var resolved = new Dictionary<string, MiniYaml>(tree.Count);
			foreach (var kv in tree)
			{
				// Inheritance is tracked from parent->child, but not from child->parentsiblings.
				var inherited = ImmutableDictionary<string, MiniYamlNode.SourceLocation>.Empty.Add(kv.Key, default);
				var children = ResolveInherits(kv.Value, tree, inherited);
				resolved.Add(kv.Key, new MiniYaml(kv.Value.Value, children));
			}

			// Resolve any top-level removals (e.g. removing whole actor blocks)
			var nodes = new MiniYaml("", resolved.Select(kv => new MiniYamlNode(kv.Key, kv.Value)));
			var result = ResolveInherits(nodes, tree, ImmutableDictionary<string, MiniYamlNode.SourceLocation>.Empty);
			return result as List<MiniYamlNode> ?? result.ToList();
		}

		static void MergeIntoResolved(MiniYamlNode overrideNode, List<MiniYamlNode> existingNodes, HashSet<string> existingNodeKeys,
			Dictionary<string, MiniYaml> tree, ImmutableDictionary<string, MiniYamlNode.SourceLocation> inherited)
		{
			var existingNodeIndex = -1;
			MiniYamlNode existingNode = null;
			if (!existingNodeKeys.Add(overrideNode.Key))
			{
				existingNodeIndex = IndexOfKey(existingNodes, overrideNode.Key);
				existingNode = existingNodes[existingNodeIndex];
			}

			var value = MergePartial(existingNode?.Value, overrideNode.Value);
			var nodes = ResolveInherits(value, tree, inherited);
			if (!value.Nodes.SequenceEqual(nodes))
				value = value.WithNodes(nodes);

			if (existingNode != null)
				existingNodes[existingNodeIndex] = existingNode.WithValue(value);
			else
				existingNodes.Add(overrideNode.WithValue(value));
		}

		static IReadOnlyCollection<MiniYamlNode> ResolveInherits(
			MiniYaml node, Dictionary<string, MiniYaml> tree, ImmutableDictionary<string, MiniYamlNode.SourceLocation> inherited)
		{
			if (node.Nodes.Length == 0)
				return node.Nodes;

			var resolved = new List<MiniYamlNode>(node.Nodes.Length);
			var resolvedKeys = new HashSet<string>(node.Nodes.Length);

			foreach (var n in node.Nodes)
			{
				if (n.Key == "Inherits" || n.Key.StartsWith("Inherits@", StringComparison.Ordinal))
				{
					if (!tree.TryGetValue(n.Value.Value, out var parent))
						throw new YamlException(
							$"{n.Location}: Parent type `{n.Value.Value}` not found");

					try
					{
						inherited = inherited.Add(n.Value.Value, n.Location);
					}
					catch (ArgumentException)
					{
						throw new YamlException(
							$"{n.Location}: Parent type `{n.Value.Value}` was already inherited by this yaml tree at {inherited[n.Value.Value]} (note: may be from a derived tree)");
					}

					foreach (var r in ResolveInherits(parent, tree, inherited))
						MergeIntoResolved(r, resolved, resolvedKeys, tree, inherited);
				}
				else if (n.Key.StartsWith('-'))
				{
					var removed = n.Key[1..];
					if (resolved.RemoveAll(r => r.Key == removed) == 0)
						throw new YamlException($"{n.Location}: There are no elements with key `{removed}` to remove");
					resolvedKeys.Remove(removed);
				}
				else
					MergeIntoResolved(n, resolved, resolvedKeys, tree, inherited);
			}

			return resolved;
		}

		/// <summary>
		/// Merges any duplicate keys that are defined within the same set of nodes.
		/// Does not resolve inheritance or node removals.
		/// </summary>
		static IReadOnlyCollection<MiniYamlNode> MergeSelfPartial(IReadOnlyCollection<MiniYamlNode> existingNodes)
		{
			if (existingNodes.Count == 0)
				return existingNodes;

			var keys = new HashSet<string>(existingNodes.Count);
			var ret = new List<MiniYamlNode>(existingNodes.Count);
			foreach (var n in existingNodes)
			{
				if (keys.Add(n.Key))
					ret.Add(n);
				else
				{
					// Node with the same key has already been added: merge new node over the existing one
					var originalIndex = IndexOfKey(ret, n.Key);
					var original = ret[originalIndex];
					ret[originalIndex] = original.WithValue(MergePartial(original.Value, n.Value));
				}
			}

			return ret;
		}

		static IReadOnlyList<MiniYamlNode> WeakResolveRemovals(IReadOnlyList<MiniYamlNode> nodes)
		{
			if (nodes == null || nodes.Count == 0)
				return nodes;

			List<MiniYamlNode> ret = null;
			for (var i = 0; i < nodes.Count; i++)
			{
				var node = nodes[i];
				if (node.Key.StartsWith('-'))
				{
					if (ret == null)
					{
						ret ??= new List<MiniYamlNode>(nodes.Count);
						ret.AddRange(nodes.Take(i));
					}

					// Apply the removal node - but "weakly" - don't throw if there is no prior node to remove.
					var removed = node.Key[1..];
					ret.RemoveAll(r => r.Key == removed);
				}
				else
				{
					ret?.Add(node);
				}
			}

			return ret ?? nodes;
		}

		static MiniYaml MergePartial(MiniYaml existingNodes, MiniYaml overrideNodes)
		{
			var resolvedExistingNodes = WeakResolveRemovals(existingNodes?.Nodes);
			var resolvedOverrideNodes = WeakResolveRemovals(overrideNodes?.Nodes);

			lock (ConflictScratch)
			{
				try
				{
					// PERF: Reuse ConflictScratch for all conflict checks to avoid allocations.
					resolvedExistingNodes?.IntoDictionaryWithConflictLog(
						n => n.Key, n => n, "MiniYaml.Merge", ConflictScratch, k => k, n => $"{n.Key} (at {n.Location})");
					resolvedOverrideNodes?.IntoDictionaryWithConflictLog(
						n => n.Key, n => n, "MiniYaml.Merge", ConflictScratch, k => k, n => $"{n.Key} (at {n.Location})");
					ConflictScratch.Clear();
				}
				catch (ArgumentException ex)
				{
					throw new YamlException(ex.Message);
				}
			}

			if (existingNodes == null)
				return overrideNodes;

			if (overrideNodes == null)
				return existingNodes;

			return new MiniYaml(overrideNodes.Value ?? existingNodes.Value, MergePartial(existingNodes.Nodes, overrideNodes.Nodes));
		}

		static IReadOnlyCollection<MiniYamlNode> MergePartial(IReadOnlyCollection<MiniYamlNode> existingNodes, IReadOnlyCollection<MiniYamlNode> overrideNodes)
		{
			if (existingNodes.Count == 0)
				return overrideNodes;

			if (overrideNodes.Count == 0)
				return existingNodes;

			var ret = new List<MiniYamlNode>(existingNodes.Count + overrideNodes.Count);
			var plainKeys = new HashSet<string>(existingNodes.Count + overrideNodes.Count);

			foreach (var node in existingNodes)
				MergeNode(node);
			foreach (var node in overrideNodes)
				MergeNode(node);

			void MergeNode(MiniYamlNode node)
			{
				// Append Removal nodes to the result.
				// Therefore: we know the remainder of the method deals with a plain node.
				if (node.Key.StartsWith('-'))
				{
					ret.Add(node);
					return;
				}

				// If no previous node with this key is present, it is new and can just be appended.
				if (plainKeys.Add(node.Key))
				{
					ret.Add(node);
					return;
				}

				// A Removal node is closer than the previous node.
				// We should not merge the new node, as the data being merged will jump before the Removal.
				// Instead, append it so the previous node is applied, then removed, then the new node is applied.
				var previousNodeIndex = LastIndexOfKey(ret, node.Key);
				var previousRemovalNodeIndex = LastIndexOfKey(ret, $"-{node.Key}");
				if (previousRemovalNodeIndex != -1 && previousRemovalNodeIndex > previousNodeIndex)
				{
					ret.Add(node);
					return;
				}

				// A previous node is present with no intervening Removal.
				// We should merge the new one into it, in place.
				ret[previousNodeIndex] = node.WithValue(MergePartial(ret[previousNodeIndex].Value, node.Value));
			}

			return ret;
		}

		static int IndexOfKey(List<MiniYamlNode> nodes, string key)
		{
			// PERF: Avoid LINQ.
			for (var i = 0; i < nodes.Count; i++)
				if (nodes[i].Key == key)
					return i;
			return -1;
		}

		static int LastIndexOfKey(List<MiniYamlNode> nodes, string key)
		{
			// PERF: Avoid LINQ.
			for (var i = nodes.Count - 1; i >= 0; i--)
				if (nodes[i].Key == key)
					return i;
			return -1;
		}

		public IEnumerable<string> ToLines(string key, string comment = null)
		{
			var hasKey = !string.IsNullOrEmpty(key);
			var hasValue = !string.IsNullOrEmpty(Value);
			var hasComment = comment != null;
			yield return (hasKey ? key + ":" : "")
				+ (hasValue ? " " + Value.Replace("#", "\\#") : "")
				+ (hasComment ? (hasKey || hasValue ? " " : "") + "#" + comment : "");

			if (Nodes != null)
				foreach (var line in Nodes.ToLines())
					yield return "\t" + line;
		}

		public static List<MiniYamlNode> Load(IReadOnlyFileSystem fileSystem, IEnumerable<string> files, MiniYaml mapRules)
		{
			if (mapRules != null && mapRules.Value != null)
			{
				var mapFiles = FieldLoader.GetValue<string[]>("value", mapRules.Value);
				files = files.Append(mapFiles);
			}

			var stringPool = new HashSet<string>(); // Reuse common strings in YAML
			IEnumerable<IReadOnlyCollection<MiniYamlNode>> yaml = files.Select(s => FromStream(fileSystem.Open(s), s, stringPool: stringPool));
			if (mapRules != null && mapRules.Nodes.Length > 0)
				yaml = yaml.Append(mapRules.Nodes);

			return Merge(yaml);
		}
	}

	public sealed class MiniYamlNodeBuilder
	{
		public MiniYamlNode.SourceLocation Location;
		public string Key;
		public MiniYamlBuilder Value;
		public string Comment;

		public MiniYamlNodeBuilder(MiniYamlNode node)
		{
			Location = node.Location;
			Key = node.Key;
			Value = new MiniYamlBuilder(node.Value);
			Comment = node.Comment;
		}

		public MiniYamlNodeBuilder(string k, MiniYamlBuilder v, string c = null)
		{
			Key = k;
			Value = v;
			Comment = c;
		}

		public MiniYamlNodeBuilder(string k, MiniYamlBuilder v, string c, MiniYamlNode.SourceLocation loc)
			: this(k, v, c)
		{
			Location = loc;
		}

		public MiniYamlNodeBuilder(string k, string v, string c = null)
			: this(k, new MiniYamlBuilder(v, null), c) { }

		public MiniYamlNodeBuilder(string k, string v, List<MiniYamlNode> n)
			: this(k, new MiniYamlBuilder(v, n), null) { }

		public MiniYamlNode Build()
		{
			return new MiniYamlNode(Key, Value.Build(), Comment, Location);
		}
	}

	public sealed class MiniYamlBuilder
	{
		public string Value;
		public List<MiniYamlNodeBuilder> Nodes;

		public MiniYamlBuilder(MiniYaml yaml)
		{
			Value = yaml.Value;
			Nodes = yaml.Nodes.Select(n => new MiniYamlNodeBuilder(n)).ToList();
		}

		public MiniYamlBuilder(string value)
			: this(value, null) { }

		public MiniYamlBuilder(string value, List<MiniYamlNode> nodes)
		{
			Value = value;
			Nodes = nodes == null ? [] : nodes.ConvertAll(x => new MiniYamlNodeBuilder(x));
		}

		public MiniYaml Build()
		{
			return new MiniYaml(Value, Nodes.Select(n => n.Build()));
		}

		public IEnumerable<string> ToLines(string key, string comment = null)
		{
			var hasKey = !string.IsNullOrEmpty(key);
			var hasValue = !string.IsNullOrEmpty(Value);
			var hasComment = comment != null;
			yield return (hasKey ? key + ":" : "")
				+ (hasValue ? " " + Value.Replace("#", "\\#") : "")
				+ (hasComment ? (hasKey || hasValue ? " " : "") + "#" + comment : "");

			if (Nodes != null)
				foreach (var line in Nodes.ToLines())
					yield return "\t" + line;
		}

		public MiniYamlNodeBuilder NodeWithKeyOrDefault(string key)
		{
			return Nodes.SingleOrDefault(n => n.Key == key);
		}
	}

	public class YamlException : Exception
	{
		public YamlException(string s)
			: base(s) { }
	}
}
