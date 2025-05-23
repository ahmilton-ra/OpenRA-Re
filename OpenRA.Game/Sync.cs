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
using System.Reflection.Emit;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA
{
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class SyncAttribute : Attribute { }

	// Marker interface
	public interface ISync { }

	public static class Sync
	{
		static readonly ConcurrentCache<Type, Func<object, int>> HashFunctions =
			new(GenerateHashFunc);

		internal static Func<object, int> GetHashFunction(ISync sync)
		{
			return HashFunctions[sync.GetType()];
		}

		internal static int Hash(ISync sync)
		{
			return GetHashFunction(sync)(sync);
		}

		static readonly Dictionary<Type, MethodInfo> CustomHashFunctions = new()
		{
			{ typeof(int2), ((Func<int2, int>)HashInt2).Method },
			{ typeof(CPos), ((Func<CPos, int>)HashCPos).Method },
			{ typeof(CVec), ((Func<CVec, int>)HashCVec).Method },
			{ typeof(WDist), ((Func<WDist, int>)HashUsingHashCode).Method },
			{ typeof(WPos), ((Func<WPos, int>)HashUsingHashCode).Method },
			{ typeof(WVec), ((Func<WVec, int>)HashUsingHashCode).Method },
			{ typeof(WAngle), ((Func<WAngle, int>)HashUsingHashCode).Method },
			{ typeof(WRot), ((Func<WRot, int>)HashUsingHashCode).Method },
			{ typeof(Actor), ((Func<Actor, int>)HashActor).Method },
			{ typeof(Player), ((Func<Player, int>)HashPlayer).Method },
			{ typeof(Target), ((Func<Target, int>)HashTarget).Method },
		};

		static void EmitSyncOpcodes(Type type, ILGenerator il)
		{
			if (CustomHashFunctions.TryGetValue(type, out var hashFunction))
				il.EmitCall(OpCodes.Call, hashFunction, null);
			else if (type == typeof(bool))
			{
				var l = il.DefineLabel();
				il.Emit(OpCodes.Ldc_I4, 0xaaa);
				il.Emit(OpCodes.Brtrue, l);
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ldc_I4, 0x555);
				il.MarkLabel(l);
			}
			else if (type != typeof(int))
				throw new NotImplementedException($"SyncAttribute on member of unhashable type: {type.FullName}");

			il.Emit(OpCodes.Xor);
		}

		static Func<object, int> GenerateHashFunc(Type t)
		{
			var d = new DynamicMethod($"hash_{t.Name}", typeof(int), [typeof(object)], t);
			var il = d.GetILGenerator();
			var this_ = il.DeclareLocal(t).LocalIndex;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, t);
			il.Emit(OpCodes.Stloc, this_);
			il.Emit(OpCodes.Ldc_I4_0);

			const BindingFlags Binding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			foreach (var field in t.GetFields(Binding).Where(x => x.HasAttribute<SyncAttribute>()))
			{
				il.Emit(OpCodes.Ldloc, this_);
				il.Emit(OpCodes.Ldfld, field);

				EmitSyncOpcodes(field.FieldType, il);
			}

			foreach (var prop in t.GetProperties(Binding).Where(x => x.HasAttribute<SyncAttribute>()))
			{
				il.Emit(OpCodes.Ldloc, this_);
				il.EmitCall(OpCodes.Call, prop.GetGetMethod(true), null);

				EmitSyncOpcodes(prop.PropertyType, il);
			}

			il.Emit(OpCodes.Ret);
			return (Func<object, int>)d.CreateDelegate(typeof(Func<object, int>));
		}

		public static int HashInt2(int2 i2)
		{
			return ((i2.X * 5) ^ (i2.Y * 3)) / 4;
		}

		public static int HashCPos(CPos i2)
		{
			return i2.Bits;
		}

		public static int HashCVec(CVec i2)
		{
			return ((i2.X * 5) ^ (i2.Y * 3)) / 4;
		}

		public static int HashActor(Actor a)
		{
			if (a != null)
				return (int)(a.ActorID << 16);
			return 0;
		}

		public static int HashPlayer(Player p)
		{
			if (p != null)
				return (int)(p.PlayerActor.ActorID << 16) * 0x567;
			return 0;
		}

		public static int HashTarget(Target t)
		{
			switch (t.Type)
			{
				case TargetType.Actor:
					return (int)(t.Actor.ActorID << 16) * 0x567;

				case TargetType.FrozenActor:
					var actor = t.FrozenActor.Actor;
					if (actor == null)
						return 0;

					return (int)(actor.ActorID << 16) * 0x567;

				case TargetType.Terrain:
					return HashUsingHashCode(t.CenterPosition);

				case TargetType.Invalid:
				default:
					return 0;
			}
		}

		public static int HashUsingHashCode<T>(T t)
		{
			return t.GetHashCode();
		}

		public static void RunUnsynced(World world, Action fn)
		{
			RunUnsynced(Game.Settings.Debug.SyncCheckUnsyncedCode, world, () => { fn(); return true; });
		}

		public static void RunUnsynced(bool checkSyncHash, World world, Action fn)
		{
			RunUnsynced(checkSyncHash, world, () => { fn(); return true; });
		}

		static int unsyncCount = 0;

		public static T RunUnsynced<T>(World world, Func<T> fn)
		{
			return RunUnsynced(Game.Settings.Debug.SyncCheckUnsyncedCode, world, fn);
		}

		public static T RunUnsynced<T>(bool checkSyncHash, World world, Func<T> fn)
		{
			unsyncCount++;

			// Detect sync changes in top level entry point only. Do not recalculate sync hash during reentry.
			var sync = unsyncCount == 1 && checkSyncHash && world != null ? world.SyncHash() : 0;

			// Running this inside a try with a finally statement means unsyncCount is decremented as soon as fn completes
			try
			{
				return fn();
			}
			finally
			{
				unsyncCount--;

				// When the world is disposing all actors and effects have been removed
				// So do not check the hash for a disposing world since it definitively has changed
				if (unsyncCount == 0 && checkSyncHash && world != null && !world.Disposing && sync != world.SyncHash())
					throw new InvalidOperationException("RunUnsynced: sync-changing code may not run here");
			}
		}

		public static void AssertUnsynced(string message)
		{
			if (unsyncCount == 0)
				throw new InvalidOperationException(message);
		}
	}
}
