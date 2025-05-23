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
using System.Linq;
using System.Reflection;

namespace OpenRA.Mods.Common.Lint
{
	sealed class CheckSyncAnnotations : ILintPass
	{
		public void Run(Action<string> emitError, Action<string> emitWarning, ModData modData)
		{
			var modTypes = modData.ObjectCreator.GetTypes();
			CheckTypes(modTypes, emitWarning);
		}

		static readonly Type SyncInterface = typeof(ISync);

		static bool TypeImplementsSync(Type type)
		{
			return type.GetInterfaces().Contains(SyncInterface);
		}

		static bool AnyTypeMemberIsSynced(Type type)
		{
			const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			while (type != null)
			{
				if (((MemberInfo[])type.GetFields(Flags)).Concat(type.GetProperties(Flags)).Any(Utility.HasAttribute<SyncAttribute>))
					return true;
				type = type.BaseType;
			}

			return false;
		}

		static void CheckTypes(IEnumerable<Type> types, Action<string> emitWarning)
		{
			foreach (var type in types)
			{
				var typeImplementsSync = TypeImplementsSync(type);
				var anyTypeMemberIsSynced = AnyTypeMemberIsSynced(type);

				if (!typeImplementsSync && anyTypeMemberIsSynced)
					emitWarning($"{type.FullName} has members with the Sync attribute but does not implement ISync.");
				else if (typeImplementsSync && !anyTypeMemberIsSynced)
					emitWarning($"{type.FullName} implements ISync but does not use the Sync attribute on any members.");
			}
		}
	}
}
