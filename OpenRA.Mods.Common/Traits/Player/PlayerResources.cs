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
using System.Globalization;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player | SystemActors.EditorPlayer)]
	public class PlayerResourcesInfo : TraitInfo, ILobbyOptions
	{
		[Desc("Descriptive label for the starting cash option in the lobby.")]
		public readonly string DefaultCashDropdownLabel = "Starting Cash";

		[Desc("Tooltip description for the starting cash option in the lobby.")]
		public readonly string DefaultCashDropdownDescription = "The amount of cash that players start with";

		[Desc("Starting cash options that are available in the lobby options.")]
		public readonly int[] SelectableCash = [2500, 5000, 10000, 20000];

		[Desc("Default starting cash option: should be one of the SelectableCash options.")]
		public readonly int DefaultCash = 5000;

		[Desc("Force the DefaultCash option by disabling changes in the lobby.")]
		public readonly bool DefaultCashDropdownLocked = false;

		[Desc("Whether to display the DefaultCash option in the lobby.")]
		public readonly bool DefaultCashDropdownVisible = true;

		[Desc("Display order for the DefaultCash option.")]
		public readonly int DefaultCashDropdownDisplayOrder = 0;

		[NotificationReference("Speech")]
		[Desc("Speech notification to play when the player does not have any funds.")]
		public readonly string InsufficientFundsNotification = null;

		[FluentReference(optional: true)]
		[Desc("Text notification to display when the player does not have any funds.")]
		public readonly string InsufficientFundsTextNotification = null;

		[Desc("Delay (in milliseconds) during which warnings will be muted.")]
		public readonly int InsufficientFundsNotificationInterval = 30000;

		[NotificationReference("Sounds")]
		public readonly string CashTickUpNotification = null;

		[NotificationReference("Sounds")]
		public readonly string CashTickDownNotification = null;

		[Desc("Monetary value of each resource type.", "Dictionary of [resource type]: [value per unit].")]
		public readonly Dictionary<string, int> ResourceValues = [];

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			var startingCash = SelectableCash.ToDictionary(c => c.ToStringInvariant(), c => "$" + c.ToString(NumberFormatInfo.CurrentInfo));

			if (startingCash.Count > 0)
				yield return new LobbyOption(map, "startingcash",
					DefaultCashDropdownLabel, DefaultCashDropdownDescription, DefaultCashDropdownVisible, DefaultCashDropdownDisplayOrder,
					startingCash, DefaultCash.ToStringInvariant(), DefaultCashDropdownLocked);
		}

		public override object Create(ActorInitializer init) { return new PlayerResources(init.Self, this); }
	}

	public class PlayerResources : ISync
	{
		public readonly PlayerResourcesInfo Info;
		readonly Player owner;

		public PlayerResources(Actor self, PlayerResourcesInfo info)
		{
			Info = info;
			owner = self.Owner;

			var startingCash = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault("startingcash", info.DefaultCash.ToStringInvariant());

			if (!int.TryParse(startingCash, out Cash))
				Cash = info.DefaultCash;

			lastNotificationTime = -Info.InsufficientFundsNotificationInterval;
		}

		[Sync]
		public int Cash;

		[Sync]
		public int Resources;

		[Sync]
		public int ResourceCapacity;

		public int Earned;
		public int Spent;

		long lastNotificationTime;

		public int ChangeCash(int amount)
		{
			if (amount >= 0)
				GiveCash(amount);
			else
			{
				// Don't put the player into negative funds
				amount = Math.Max(-GetCashAndResources(), amount);

				TakeCash(-amount);
			}

			return amount;
		}

		public bool CanGiveResources(int amount)
		{
			return Resources + amount <= ResourceCapacity;
		}

		public void GiveResources(int num)
		{
			Resources += num;
			Earned += num;

			if (Resources > ResourceCapacity)
			{
				Earned -= Resources - ResourceCapacity;
				Resources = ResourceCapacity;
			}
		}

		public bool TakeResources(int num)
		{
			if (Resources < num) return false;
			Resources -= num;
			Spent += num;

			return true;
		}

		public void GiveCash(int num)
		{
			if (Cash < int.MaxValue)
			{
				try
				{
					checked
					{
						Cash += num;
					}
				}
				catch (OverflowException)
				{
					Cash = int.MaxValue;
				}
			}

			if (Earned < int.MaxValue)
			{
				try
				{
					checked
					{
						Earned += num;
					}
				}
				catch (OverflowException)
				{
					Earned = int.MaxValue;
				}
			}
		}

		public bool TakeCash(int num, bool notifyLowFunds = false)
		{
			if (GetCashAndResources() < num)
			{
				if (notifyLowFunds && Game.RunTime > lastNotificationTime + Info.InsufficientFundsNotificationInterval)
				{
					lastNotificationTime = Game.RunTime;
					Game.Sound.PlayNotification(owner.World.Map.Rules, owner, "Speech", Info.InsufficientFundsNotification, owner.Faction.InternalName);
					TextNotificationsManager.AddTransientLine(owner, Info.InsufficientFundsTextNotification);
				}

				return false;
			}

			// Spend ore before cash
			Resources -= num;
			Spent += num;
			if (Resources < 0)
			{
				Cash += Resources;
				Resources = 0;
			}

			return true;
		}

		public void AddStorageCapacity(int capacity)
		{
			ResourceCapacity += capacity;
		}

		public void RemoveStorageCapacity(int capacity)
		{
			ResourceCapacity -= capacity;

			if (Resources > ResourceCapacity)
				Resources = ResourceCapacity;
		}

		public int GetCashAndResources()
		{
			return Cash + Resources;
		}
	}
}
