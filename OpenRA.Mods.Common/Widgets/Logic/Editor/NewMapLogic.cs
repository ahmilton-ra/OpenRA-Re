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
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class NewMapLogic : ChromeLogic
	{
		readonly Widget panel;

		[ObjectCreator.UseCtor]
		public NewMapLogic(Action onExit, Action<string> onSelect, Widget widget, World world, ModData modData)
		{
			panel = widget;

			panel.Get<ButtonWidget>("CANCEL_BUTTON").OnClick = () => { Ui.CloseWindow(); onExit(); };

			var tilesetDropDown = panel.Get<DropDownButtonWidget>("TILESET");
			var tilesets = modData.DefaultTerrainInfo.Keys;
			ScrollItemWidget SetupItem(string option, ScrollItemWidget template)
			{
				var item = ScrollItemWidget.Setup(template,
					() => tilesetDropDown.GetText() == option,
					() => tilesetDropDown.GetText = () => option);
				item.Get<LabelWidget>("LABEL").GetText = () => option;
				return item;
			}

			var firstTileset = tilesets.First();
			tilesetDropDown.GetText = () => firstTileset;
			tilesetDropDown.OnClick = () =>
				tilesetDropDown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, tilesets, SetupItem);

			var widthTextField = panel.Get<TextFieldWidget>("WIDTH");
			var heightTextField = panel.Get<TextFieldWidget>("HEIGHT");

			panel.Get<ButtonWidget>("CREATE_BUTTON").OnClick = () =>
			{
				int.TryParse(widthTextField.Text, out var width);
				int.TryParse(heightTextField.Text, out var height);

				// Require at least a 2x2 playable area so that the
				// ground is visible through the edge shroud
				width = Math.Max(2, width);
				height = Math.Max(2, height);

				var maxTerrainHeight = world.Map.Grid.MaximumTerrainHeight;
				var tileset = modData.DefaultTerrainInfo[tilesetDropDown.GetText()];
				var map = new Map(Game.ModData, tileset, width + 2, height + maxTerrainHeight + 2);

				var tl = new PPos(1, 1 + maxTerrainHeight);
				var br = new PPos(width, height + maxTerrainHeight);
				map.SetBounds(tl, br);

				map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();

				if (map.Rules.TerrainInfo is ITerrainInfoNotifyMapCreated notifyMapCreated)
					notifyMapCreated.MapCreated(map);

				var package = new ZipFileLoader.ReadWriteZipFile();
				map.Save(package);
				map = new Map(modData, package);
				Game.LoadEditor(map);
				Ui.CloseWindow();
				onSelect(map.Uid);
			};
		}
	}
}
