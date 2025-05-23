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
using System.Linq;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class CreateManPage : IUtilityCommand
	{
		string IUtilityCommand.Name => "--man-page";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return true;
		}

		[Desc("Create a man page in troff format.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Console.WriteLine(".TH OPENRA 6");
			Console.WriteLine(".SH NAME");
			Console.WriteLine("openra \\- An Open Source modernization of the early 2D Command & Conquer games.");
			Console.WriteLine(".SH SYNOPSIS");
			Console.WriteLine(".B openra");
			Console.WriteLine("[\\fB\\Game.Mod=\\fR\\fIra\\fR]");
			Console.WriteLine(".SH DESCRIPTION");
			Console.WriteLine(".B openra");
			Console.WriteLine("starts the game.");
			Console.WriteLine(".SH OPTIONS");

			var sections = Game.Settings.Sections;
			sections.Add("Launch", new LaunchArguments(new Arguments([])));
			foreach (var section in sections.OrderBy(s => s.Key))
			{
				var fields = Utility.GetFields(section.Value.GetType());
				foreach (var field in fields)
				{
					if (!Utility.HasAttribute<DescAttribute>(field))
						continue;

					Console.WriteLine(".TP");

					Console.Write($".BR {section.Key}.{field.Name}=");
					var value = field.GetValue(section.Value);
					if (value != null && !value.ToString().StartsWith("System.", StringComparison.Ordinal))
						Console.WriteLine($"\\fI{value}\\fR");
					else
						Console.WriteLine();

					var lines = Utility.GetCustomAttributes<DescAttribute>(field, false).SelectMany(d => d.Lines);
					foreach (var line in lines)
						Console.WriteLine(line);
				}
			}

			Console.WriteLine(".SH FILES");
			Console.WriteLine("Settings are stored in the ~/.openra user folder.");
			Console.WriteLine(".SH BUGS");
			Console.WriteLine("Known issues are tracked at https://bugs.openra.net");
			Console.WriteLine(".SH COPYRIGHT");
			Console.WriteLine("Copyright (c) The OpenRA Developers and Contributors");
			Console.WriteLine("This manual is part of OpenRA, which is free software. It is GNU GPL v3 licensed. See COPYING for details.");
		}
	}
}
