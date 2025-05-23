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
using Eluant;
using OpenRA.Effects;
using OpenRA.Scripting;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptGlobal("Trigger")]
	public class TriggerGlobal : ScriptGlobal
	{
		public TriggerGlobal(ScriptContext context)
			: base(context) { }

		public static ScriptTriggers GetScriptTriggers(Actor actor)
		{
			var events = actor.TraitOrDefault<ScriptTriggers>();
			if (events == null)
				throw new LuaException($"Actor '{actor.Info.Name}' requires the ScriptTriggers trait before attaching a trigger");

			return events;
		}

		[Desc("Call a function after a specified delay. The callback function will be called as func().")]
		public void AfterDelay(int delay, [ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			var f = (LuaFunction)func.CopyReference();
			void DoCall()
			{
				try
				{
					using (f)
						f.Call().Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			Context.World.AddFrameEndTask(w => w.Add(new DelayedAction(delay, DoCall)));
		}

		[Desc("Call a function for each passenger when it enters a transport. " +
			"The callback function will be called as func(transport: actor, passenger: actor).")]
		public void OnPassengerEntered(Actor actor, [ScriptEmmyTypeOverride("fun(transport: actor, passenger: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnPassengerEntered, func, Context);
		}

		[Desc("Call a function for each passenger when it exits a transport. " +
			"The callback function will be called as func(transport: actor, passenger: actor).")]
		public void OnPassengerExited(Actor actor, [ScriptEmmyTypeOverride("fun(transport: actor, passenger: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnPassengerExited, func, Context);
		}

		[Desc("Call a function each tick that the actor is idle. " +
			"The callback function will be called as func(self: actor).")]
		public void OnIdle(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnIdle, func, Context);
		}

		[Desc("Call a function when the actor is damaged. " +
			"Repairs or other negative damage can activate this trigger. The callback " +
			"function will be called as func(self: actor, attacker: actor, damage: integer).")]
		public void OnDamaged(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor, attacker: actor, damage: integer)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnDamaged, func, Context);
		}

		[Desc("Call a function when the actor is killed. The callback " +
			"function will be called as func(self: actor, killer: actor).")]
		public void OnKilled(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor, killer: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnKilled, func, Context);
		}

		[Desc("Call a function when all of the actors in a group are killed. The callback " +
			"function will be called as func().")]
		public void OnAllKilled(Actor[] actors, [ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			if (actors == null)
				throw new NullReferenceException(nameof(actors));

			var group = actors.ToList();
			var f = (LuaFunction)func.CopyReference();
			void OnMemberKilled(Actor m)
			{
				try
				{
					group.Remove(m);
					if (group.Count == 0)
						using (f)
							f.Call();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			foreach (var a in group)
				GetScriptTriggers(a).OnKilledInternal += OnMemberKilled;
		}

		[Desc("Call a function when one of the actors in a group is killed. " +
			"This trigger is only called once. The callback " +
			"function will be called as func(killed: actor).")]
		public void OnAnyKilled(Actor[] actors, [ScriptEmmyTypeOverride("fun(killed: actor)")] LuaFunction func)
		{
			var called = false;
			var f = (LuaFunction)func.CopyReference();
			void OnMemberKilled(Actor m)
			{
				try
				{
					if (called)
						return;

					using (f)
					using (var killed = m.ToLuaValue(Context))
						f.Call(killed).Dispose();

					called = true;
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			if (actors == null)
				throw new NullReferenceException(nameof(actors));

			foreach (var a in actors)
				GetScriptTriggers(a).OnKilledInternal += OnMemberKilled;
		}

		[Desc("Call a function when this actor produces another actor. " +
			"The callback function will be called as func(producer: actor, produced: actor).")]
		public void OnProduction(Actor actor, [ScriptEmmyTypeOverride("fun(producer: actor, produced: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnProduction, func, Context);
		}

		[Desc("Call a function when any actor produces another actor. The callback " +
			"function will be called as func(producer: actor, produced: actor, productionType: string).")]
		public void OnAnyProduction([ScriptEmmyTypeOverride("fun(producer: actor, produced: actor, productionType: string)")] LuaFunction func)
		{
			GetScriptTriggers(Context.World.WorldActor).RegisterCallback(Trigger.OnOtherProduction, func, Context);
		}

		[Desc("Call a function when this player completes all primary objectives. " +
			"The callback function will be called as func(p: player).")]
		public void OnPlayerWon(Player player, [ScriptEmmyTypeOverride("fun(p: player)")] LuaFunction func)
		{
			if (player == null)
				throw new NullReferenceException(nameof(player));

			GetScriptTriggers(player.PlayerActor).RegisterCallback(Trigger.OnPlayerWon, func, Context);
		}

		[Desc("Call a function when this player fails any primary objective. " +
			"The callback function will be called as func(p: player).")]
		public void OnPlayerLost(Player player, [ScriptEmmyTypeOverride("fun(p: player)")] LuaFunction func)
		{
			if (player == null)
				throw new NullReferenceException(nameof(player));

			GetScriptTriggers(player.PlayerActor).RegisterCallback(Trigger.OnPlayerLost, func, Context);
		}

		[Desc("Call a function when this player is assigned a new objective. " +
			"The callback function will be called as func(p: player, objectiveId: integer).")]
		public void OnObjectiveAdded(Player player, [ScriptEmmyTypeOverride("fun(p: player, objectiveId: integer)")] LuaFunction func)
		{
			if (player == null)
				throw new NullReferenceException(nameof(player));

			GetScriptTriggers(player.PlayerActor).RegisterCallback(Trigger.OnObjectiveAdded, func, Context);
		}

		[Desc("Call a function when this player completes an objective. " +
			"The callback function will be called as func(p: player, objectiveId: integer).")]
		public void OnObjectiveCompleted(Player player, [ScriptEmmyTypeOverride("fun(p: player, objectiveId: integer)")] LuaFunction func)
		{
			if (player == null)
				throw new NullReferenceException(nameof(player));

			GetScriptTriggers(player.PlayerActor).RegisterCallback(Trigger.OnObjectiveCompleted, func, Context);
		}

		[Desc("Call a function when this player fails an objective. " +
			"The callback function will be called as func(p: player, objectiveId: integer).")]
		public void OnObjectiveFailed(Player player, [ScriptEmmyTypeOverride("fun(p: player, objectiveId: integer)")] LuaFunction func)
		{
			if (player == null)
				throw new NullReferenceException(nameof(player));

			GetScriptTriggers(player.PlayerActor).RegisterCallback(Trigger.OnObjectiveFailed, func, Context);
		}

		[Desc("Call a function when this actor is added to the world. " +
			"The callback function will be called as func(self: actor).")]
		public void OnAddedToWorld(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnAddedToWorld, func, Context);
		}

		[Desc("Call a function when this actor is removed from the world. " +
			"The callback function will be called as func(self: actor).")]
		public void OnRemovedFromWorld(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnRemovedFromWorld, func, Context);
		}

		[Desc("Call a function when all of the actors in a group have been removed from the world. " +
			"The callback function will be called as func().")]
		public void OnAllRemovedFromWorld(Actor[] actors, [ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			if (actors == null)
				throw new NullReferenceException(nameof(actors));

			var group = actors.ToList();

			var f = (LuaFunction)func.CopyReference();
			void OnMemberRemoved(Actor m)
			{
				try
				{
					if (!group.Remove(m))
						return;

					if (group.Count == 0)
					{
						// Functions can only be .Call()ed once, so operate on a copy so we can reuse it later
						var temp = (LuaFunction)f.CopyReference();
						using (temp)
							temp.Call().Dispose();
					}
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			void OnMemberAdded(Actor m)
			{
				try
				{
					if (!actors.Contains(m) || group.Contains(m))
						return;

					group.Add(m);
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			foreach (var a in group)
			{
				GetScriptTriggers(a).OnRemovedInternal += OnMemberRemoved;
				GetScriptTriggers(a).OnAddedInternal += OnMemberAdded;
			}
		}

		[Desc("Call a function when this actor is captured. The callback function " +
			"will be called as func(self: actor, captor: actor, oldOwner: player, newOwner: player).")]
		public void OnCapture(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor, captor: actor, oldOwner: player, newOwner: player)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnCapture, func, Context);
		}

		[Desc("Call a function when this actor is killed or captured. " +
			"This trigger is only called once. " +
			"The callback function will be called as func().")]
		public void OnKilledOrCaptured(Actor actor, [ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			var called = false;

			var f = (LuaFunction)func.CopyReference();
			void OnKilledOrCaptured(Actor m)
			{
				try
				{
					if (called)
						return;

					using (f)
						f.Call().Dispose();

					called = true;
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).OnCapturedInternal += OnKilledOrCaptured;
			GetScriptTriggers(actor).OnKilledInternal += OnKilledOrCaptured;
		}

		[Desc("Call a function when all of the actors in a group have been killed or captured. " +
			"This trigger is only called once. " +
			"The callback function will be called as func().")]
		public void OnAllKilledOrCaptured(Actor[] actors, [ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			if (actors == null)
				throw new NullReferenceException(nameof(actors));

			var group = actors.ToList();

			var f = (LuaFunction)func.CopyReference();
			void OnMemberKilledOrCaptured(Actor m)
			{
				try
				{
					if (!group.Remove(m))
						return;

					if (group.Count == 0)
						using (f)
							f.Call().Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			foreach (var a in group)
			{
				GetScriptTriggers(a).OnCapturedInternal += OnMemberKilledOrCaptured;
				GetScriptTriggers(a).OnKilledInternal += OnMemberKilledOrCaptured;
			}
		}

		[Desc("Call a function when a ground-based actor enters this cell footprint. " +
			"Returns the trigger ID for later removal using RemoveFootprintTrigger(id: integer). " +
			"The callback function will be called as func(a: actor, id: integer).")]
		public int OnEnteredFootprint(CPos[] cells, [ScriptEmmyTypeOverride("fun(a: actor, id: integer)")] LuaFunction func)
		{
			// We can't easily dispose onEntry, so we'll have to rely on finalization for it.
			var onEntry = (LuaFunction)func.CopyReference();
			var triggerId = 0;
			void InvokeEntry(Actor a)
			{
				try
				{
					using (var luaActor = a.ToLuaValue(Context))
					using (var id = triggerId.ToLuaValue(Context))
						onEntry.Call(luaActor, id).Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			triggerId = Context.World.ActorMap.AddCellTrigger(cells, InvokeEntry, null);

			return triggerId;
		}

		[Desc("Call a function when a ground-based actor leaves this cell footprint. " +
			"Returns the trigger ID for later removal using RemoveFootprintTrigger(id: integer). " +
			"The callback function will be called as func(a: actor, id: integer).")]
		public int OnExitedFootprint(CPos[] cells, [ScriptEmmyTypeOverride("fun(a: actor, id: integer)")] LuaFunction func)
		{
			// We can't easily dispose onExit, so we'll have to rely on finalization for it.
			var onExit = (LuaFunction)func.CopyReference();
			var triggerId = 0;
			void InvokeExit(Actor a)
			{
				try
				{
					using (var luaActor = a.ToLuaValue(Context))
					using (var id = triggerId.ToLuaValue(Context))
						onExit.Call(luaActor, id).Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			triggerId = Context.World.ActorMap.AddCellTrigger(cells, null, InvokeExit);

			return triggerId;
		}

		[Desc("Removes a previously created footprint trigger.")]
		public void RemoveFootprintTrigger(int id)
		{
			Context.World.ActorMap.RemoveCellTrigger(id);
		}

		[Desc("Call a function when an actor enters this range. " +
			"Returns the trigger ID for later removal using RemoveProximityTrigger(id: integer). " +
			"The callback function will be called as func(a: actor, id: integer).")]
		public int OnEnteredProximityTrigger(WPos pos, WDist range, [ScriptEmmyTypeOverride("fun(a: actor, id: integer)")] LuaFunction func)
		{
			// We can't easily dispose onEntry, so we'll have to rely on finalization for it.
			var onEntry = (LuaFunction)func.CopyReference();
			var triggerId = 0;
			void InvokeEntry(Actor a)
			{
				try
				{
					using (var luaActor = a.ToLuaValue(Context))
					using (var id = triggerId.ToLuaValue(Context))
						onEntry.Call(luaActor, id).Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			triggerId = Context.World.ActorMap.AddProximityTrigger(pos, range, WDist.Zero, InvokeEntry, null);

			return triggerId;
		}

		[Desc("Call a function when an actor leaves this range. " +
			"Returns the trigger ID for later removal using RemoveProximityTrigger(id: integer). " +
			"The callback function will be called as func(a: actor, id: integer).")]
		public int OnExitedProximityTrigger(WPos pos, WDist range, [ScriptEmmyTypeOverride("fun(a: actor, id: integer)")] LuaFunction func)
		{
			// We can't easily dispose onExit, so we'll have to rely on finalization for it.
			var onExit = (LuaFunction)func.CopyReference();
			var triggerId = 0;
			void InvokeExit(Actor a)
			{
				try
				{
					using (var luaActor = a.ToLuaValue(Context))
					using (var id = triggerId.ToLuaValue(Context))
						onExit.Call(luaActor, id).Dispose();
				}
				catch (Exception e)
				{
					Context.FatalError(e);
				}
			}

			triggerId = Context.World.ActorMap.AddProximityTrigger(pos, range, WDist.Zero, null, InvokeExit);

			return triggerId;
		}

		[Desc("Removes a previously created proximity trigger.")]
		public void RemoveProximityTrigger(int id)
		{
			Context.World.ActorMap.RemoveProximityTrigger(id);
		}

		[Desc("Call a function when this actor is infiltrated. The callback function " +
			"will be called as func(self: actor, infiltrator: actor).")]
		public void OnInfiltrated(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor, infiltrator: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnInfiltrated, func, Context);
		}

		[Desc("Call a function when this actor is discovered by an enemy or a player with a Neutral stance. " +
			"The callback function will be called as func(discovered: actor, discoverer: player). " +
			"The player actor needs the 'EnemyWatcher' trait. The actors to discover need the 'AnnounceOnSeen' trait.")]
		public void OnDiscovered(Actor actor, [ScriptEmmyTypeOverride("fun(discovered: actor, discoverer: player)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnDiscovered, func, Context);
		}

		[Desc("Call a function when this player is discovered by an enemy or neutral player. " +
			"The callback function will be called as func(discovered: player, discoverer: player, discoveredActor: actor)." +
			"The player actor needs the 'EnemyWatcher' trait. The actors to discover need the 'AnnounceOnSeen' trait.")]
		public void OnPlayerDiscovered(
			Player discovered, [ScriptEmmyTypeOverride("fun(discovered: player, discoverer: player, discoveredActor: actor)")] LuaFunction func)
		{
			if (discovered == null)
				throw new NullReferenceException(nameof(discovered));

			GetScriptTriggers(discovered.PlayerActor).RegisterCallback(Trigger.OnPlayerDiscovered, func, Context);
		}

		[Desc("Call a function when this actor is sold. The callback function " +
			"will be called as func(self: actor).")]
		public void OnSold(Actor actor, [ScriptEmmyTypeOverride("fun(self: actor)")] LuaFunction func)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).RegisterCallback(Trigger.OnSold, func, Context);
		}

		[Desc("Call a function when the game timer expires. The callback function will be called as func().")]
		public void OnTimerExpired([ScriptEmmyTypeOverride("fun()")] LuaFunction func)
		{
			GetScriptTriggers(Context.World.WorldActor).RegisterCallback(Trigger.OnTimerExpired, func, Context);
		}

		[Desc("Removes all triggers from this actor. " +
			"Note that the removal will only take effect at the end of a tick, " +
			"so you must not add new triggers at the same time that you are calling this function.")]
		public void ClearAll(Actor actor)
		{
			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).ClearAll();
		}

		[Desc("Removes the specified trigger from this actor. " +
			"Note that the removal will only take effect at the end of a tick, " +
			"so you must not add new triggers at the same time that you are calling this function.")]
		public void Clear(Actor actor, string triggerName)
		{
			var trigger = Enum.Parse<Trigger>(triggerName);

			if (actor == null)
				throw new NullReferenceException(nameof(actor));

			GetScriptTriggers(actor).Clear(trigger);
		}
	}
}
