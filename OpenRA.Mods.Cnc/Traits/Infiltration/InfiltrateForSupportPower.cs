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

using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	sealed class InfiltrateForSupportPowerInfo : TraitInfo
	{
		[ActorReference]
		[FieldLoader.Require]
		public readonly string Proxy = null;

		[Desc("The `TargetTypes` from `Targetable` that are allowed to enter.")]
		public readonly BitSet<TargetableType> Types = default;

		[Desc("Experience to grant to the infiltrating player.")]
		public readonly int PlayerExperience = 0;

		[NotificationReference("Speech")]
		[Desc("Sound the victim will hear when technology gets stolen.")]
		public readonly string InfiltratedNotification = null;

		[FluentReference(optional: true)]
		[Desc("Text notification the victim will see when technology gets stolen.")]
		public readonly string InfiltratedTextNotification = null;

		[NotificationReference("Speech")]
		[Desc("Sound the perpetrator will hear after successful infiltration.")]
		public readonly string InfiltrationNotification = null;

		[FluentReference(optional: true)]
		[Desc("Text notification the perpetrator will see after successful infiltration.")]
		public readonly string InfiltrationTextNotification = null;

		public override object Create(ActorInitializer init) { return new InfiltrateForSupportPower(this); }
	}

	sealed class InfiltrateForSupportPower : INotifyInfiltrated
	{
		readonly InfiltrateForSupportPowerInfo info;

		public InfiltrateForSupportPower(InfiltrateForSupportPowerInfo info)
		{
			this.info = info;
		}

		void INotifyInfiltrated.Infiltrated(Actor self, Actor infiltrator, BitSet<TargetableType> types)
		{
			if (!info.Types.Overlaps(types))
				return;

			if (info.InfiltratedNotification != null)
				Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", info.InfiltratedNotification, self.Owner.Faction.InternalName);

			if (info.InfiltrationNotification != null)
				Game.Sound.PlayNotification(self.World.Map.Rules, infiltrator.Owner, "Speech", info.InfiltrationNotification, infiltrator.Owner.Faction.InternalName);

			TextNotificationsManager.AddTransientLine(self.Owner, info.InfiltratedTextNotification);
			TextNotificationsManager.AddTransientLine(infiltrator.Owner, info.InfiltrationTextNotification);

			infiltrator.Owner.PlayerActor.TraitOrDefault<PlayerExperience>()?.GiveExperience(info.PlayerExperience);

			infiltrator.World.AddFrameEndTask(w => w.CreateActor(info.Proxy,
			[
				new OwnerInit(infiltrator.Owner)
			]));
		}
	}
}
