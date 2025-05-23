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

using System.Collections.Generic;

namespace OpenRA
{
	public class PlayerProfile
	{
		public readonly string Fingerprint;
		public readonly string PublicKey;
		public readonly bool KeyRevoked;

		public readonly int ProfileID;
		public readonly string ProfileName;
		public readonly string ProfileRank = "Registered Player";

		[FieldLoader.LoadUsing(nameof(LoadBadges))]
		public readonly List<PlayerBadge> Badges;

		static object LoadBadges(MiniYaml yaml)
		{
			var badges = new List<PlayerBadge>();

			var badgesNode = yaml.NodeWithKeyOrDefault("Badges");
			if (badgesNode != null)
			{
				var playerDatabase = Game.ModData.Manifest.Get<PlayerDatabase>();
				foreach (var badgeNode in badgesNode.Value.Nodes)
				{
					Game.RunAfterTick(() =>
					{
						// Discard badge on error
						try
						{
							var badge = playerDatabase.LoadBadge(badgeNode.Value);
							if (badge != null)
								badges.Add(badge);
						}
						catch { }
					});
				}
			}

			return badges;
		}
	}

	public record PlayerBadge(string Label, string Icon, string Icon2x, string Icon3x);
}
