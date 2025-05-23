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
using System.Linq;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Lint
{
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class ChromeLogicArgsHotkeys(params string[] logicArgKeys) : Attribute
	{
		public string[] LogicArgKeys = logicArgKeys;
	}

	[AttributeUsage(AttributeTargets.Method)]
	public sealed class CustomLintableHotkeyNames : Attribute { }

	sealed class CheckChromeHotkeys : ILintPass
	{
		public void Run(Action<string> emitError, Action<string> emitWarning, ModData modData)
		{
			// Build the list of valid hotkey names.
			var namedKeys = modData.Hotkeys.Definitions.Select(d => d.Name).ToArray();

			// Build the list of widget keys to validate.
			var checkWidgetFields = modData.ObjectCreator.GetTypesImplementing<Widget>()
				.SelectMany(w => Utility.GetFields(w)
					.Where(f => f.FieldType == typeof(HotkeyReference))
					.Select(f => (w.Name[..^6], f.Name)))
				.ToArray();

			var customLintMethods = new Dictionary<string, List<string>>();

			foreach (var w in modData.ObjectCreator.GetTypesImplementing<Widget>())
			{
				foreach (var m in w.GetMethods().Where(Utility.HasAttribute<CustomLintableHotkeyNames>))
				{
					var p = m.GetParameters();
					if (p.Length == 3 && p[0].ParameterType == typeof(MiniYamlNode) && p[1].ParameterType == typeof(Action<string>)
							&& p[2].ParameterType == typeof(Action<string>))
						customLintMethods.GetOrAdd(w.Name[..^6]).Add(m.Name);
				}
			}

			foreach (var filename in modData.Manifest.ChromeLayout)
			{
				var yaml = MiniYaml.FromStream(modData.DefaultFileSystem.Open(filename), filename);
				CheckInner(modData, namedKeys, checkWidgetFields, customLintMethods, yaml, filename, null, emitError);
			}
		}

		static void CheckInner(
			ModData modData,
			string[] namedKeys,
			(string Widget, string Field)[] checkWidgetFields,
			Dictionary<string, List<string>> customLintMethods,
			IEnumerable<MiniYamlNode> nodes,
			string filename,
			MiniYamlNode parent,
			Action<string> emitError)
		{
			foreach (var node in nodes)
			{
				if (node.Value == null)
					continue;

				foreach (var x in checkWidgetFields)
				{
					if (node.Key == x.Field && parent != null && parent.Key.StartsWith(x.Widget, StringComparison.Ordinal))
					{
						// Keys are valid if they refer to a named key or can be parsed as a regular Hotkey.
						if (!namedKeys.Contains(node.Value.Value) && !Hotkey.TryParse(node.Value.Value, out var unused))
							emitError($"{node.Location} refers to a Key named `{node.Value.Value}` that does not exist.");
					}
				}

				// Check runtime-defined hotkey names.
				var widgetType = node.Key.Split('@')[0];
				if (customLintMethods.TryGetValue(widgetType, out var checkMethods))
				{
					var type = modData.ObjectCreator.FindType(widgetType + "Widget");
					var keyNames = checkMethods.SelectMany(m => (IEnumerable<string>)type.GetMethod(m).Invoke(null, [node, emitError]));

					foreach (var name in keyNames)
						if (!namedKeys.Contains(name) && !Hotkey.TryParse(name, out var unused))
							emitError($"{node.Location} refers to a Key named `{name}` that does not exist.");
				}

				// Logic classes can declare the data key names that specify hotkeys.
				if (node.Key == "Logic" && node.Value.Nodes.Length > 0)
				{
					var typeNames = FieldLoader.GetValue<string[]>(node.Key, node.Value.Value);
					var checkArgKeys = new List<string>();
					foreach (var typeName in typeNames)
					{
						var type = Game.ModData.ObjectCreator.FindType(typeName);
						if (type == null)
							continue;

						checkArgKeys.AddRange(Utility.GetCustomAttributes<ChromeLogicArgsHotkeys>(type, true).SelectMany(x => x.LogicArgKeys));
					}

					foreach (var n in node.Value.Nodes)
						if (checkArgKeys.Contains(n.Key) &&
							!namedKeys.Contains(n.Value.Value) &&
							!Hotkey.TryParse(n.Value.Value, out var unused))
							emitError($"{filename} {node.Value.Value}:{n.Key} refers to a Key named `{n.Value.Value}` that does not exist.");
				}

				if (node.Value.Nodes != null)
					CheckInner(modData, namedKeys, checkWidgetFields, customLintMethods, node.Value.Nodes, filename, node, emitError);
			}
		}
	}
}
