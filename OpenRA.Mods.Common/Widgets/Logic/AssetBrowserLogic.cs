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
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Video;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class AssetBrowserLogic : ChromeLogic
	{
		[Flags]
		enum AssetType
		{
			Sprite = 1,
			Model = 2,
			Audio = 4,
			Video = 8,
			Unknown = 16
		}

		[FluentReference("length")]
		const string LengthInSeconds = "label-length-in-seconds";

		[FluentReference]
		const string AllPackages = "label-all-packages";

		readonly string[] allowedExtensions;
		readonly string[] allowedSpriteExtensions;
		readonly string[] allowedModelExtensions;
		readonly string[] allowedAudioExtensions;
		readonly string[] allowedVideoExtensions;
		readonly IEnumerable<IReadOnlyPackage> acceptablePackages;
		readonly string[] palettes;
		readonly World world;
		readonly ModData modData;

		readonly Widget panel;

		readonly TextFieldWidget filenameInput;
		readonly SliderWidget frameSlider;
		readonly ScrollPanelWidget assetList;
		readonly ScrollItemWidget template;

		readonly Cache<SheetType, SheetBuilder> sheetBuilders;
		readonly Cache<string, Sprite[]> spriteCache;

		IReadOnlyPackage assetSource = null;
		bool animateFrames = false;

		string currentPalette;
		string currentFilename;
		IReadOnlyPackage currentPackage;
		Sprite[] currentSprites;
		IModel currentVoxel;
		ISound currentSound;
		ISoundFormat currentSoundFormat;
		Stream currentAudioStream;
		VideoPlayerWidget player = null;
		bool isVideoLoaded = false;
		bool isLoadError = false;
		int currentFrame;
		WRot modelOrientation;
		float spriteScale;
		float modelScale;
		AssetType assetTypesToDisplay = AssetType.Sprite | AssetType.Model | AssetType.Audio | AssetType.Video;

		readonly string allPackages;

		[ObjectCreator.UseCtor]
		public AssetBrowserLogic(Widget widget, Action onExit, ModData modData, WorldRenderer worldRenderer)
		{
			sheetBuilders = new Cache<SheetType, SheetBuilder>(t => new SheetBuilder(t));
			spriteCache = new Cache<string, Sprite[]>(
				filename => FrameLoader.GetFrames(modData.DefaultFileSystem, filename, modData.SpriteLoaders, out _)
						.Select(f => sheetBuilders[SheetBuilder.FrameTypeToSheetType(f.Type)].Add(f))
						.ToArray());

			world = worldRenderer.World;
			this.modData = modData;
			panel = widget;

			allPackages = FluentProvider.GetMessage(AllPackages);

			var colorPickerPalettes = world.WorldActor.TraitsImplementing<IProvidesAssetBrowserColorPickerPalettes>()
				.SelectMany(p => p.ColorPickerPaletteNames)
				.ToArray();

			palettes = world.WorldActor.TraitsImplementing<IProvidesAssetBrowserPalettes>()
				.SelectMany(p => p.PaletteNames)
				.Concat(colorPickerPalettes)
				.ToArray();

			var ticker = panel.GetOrNull<LogicTickerWidget>("ANIMATION_TICKER");
			if (ticker != null)
			{
				ticker.OnTick = () =>
				{
					if (animateFrames && currentSprites != null)
						SelectNextFrame();
				};
			}

			var sourceDropdown = panel.GetOrNull<DropDownButtonWidget>("SOURCE_SELECTOR");
			if (sourceDropdown != null)
			{
				sourceDropdown.OnMouseDown = _ => ShowSourceDropdown(sourceDropdown);
				var sourceName = new CachedTransform<IReadOnlyPackage, string>(GetSourceDisplayName);
				sourceDropdown.GetText = () => sourceName.Update(assetSource);
			}

			var assetTypeDropdown = panel.GetOrNull<DropDownButtonWidget>("ASSET_TYPES_DROPDOWN");
			if (assetTypeDropdown != null)
			{
				var assetTypesPanel = CreateAssetTypesPanel();
				assetTypeDropdown.OnMouseDown = _ =>
				{
					assetTypeDropdown.RemovePanel();
					assetTypeDropdown.AttachPanel(assetTypesPanel);
				};
			}

			var spriteWidget = panel.GetOrNull<SpriteWidget>("SPRITE");
			if (spriteWidget != null)
			{
				spriteWidget.GetSprite = () => currentSprites?.Length > 0 ? currentSprites[currentFrame] : null;
				currentPalette = spriteWidget.Palette;
				spriteScale = spriteWidget.Scale;
				spriteWidget.GetPalette = () => currentPalette;
				spriteWidget.IsVisible = () => !isVideoLoaded && !isLoadError && currentSprites != null;
				spriteWidget.GetScale = () => spriteScale;
			}

			var playerWidget = panel.GetOrNull<VideoPlayerWidget>("PLAYER");
			if (playerWidget != null)
				playerWidget.IsVisible = () => isVideoLoaded && !isLoadError;

			if (panel.GetOrNull<Widget>("VOXEL") is IModelWidget modelWidget)
			{
				currentPalette = modelWidget.Palette;
				modelScale = modelWidget.Scale;
				modelWidget.Setup(
					() => !isVideoLoaded && !isLoadError && currentVoxel != null,
					() => currentPalette,
					() => currentPalette,
					() => modelScale,
					() => currentVoxel,
					() => modelOrientation);
			}

			var errorLabelWidget = panel.GetOrNull("ERROR");
			if (errorLabelWidget != null)
				errorLabelWidget.IsVisible = () => isLoadError;

			var paletteDropDown = panel.GetOrNull<DropDownButtonWidget>("PALETTE_SELECTOR");
			if (paletteDropDown != null)
			{
				paletteDropDown.OnMouseDown = _ => ShowPaletteDropdown(paletteDropDown);
				paletteDropDown.GetText = () => currentPalette;
				paletteDropDown.IsVisible = () => currentSprites != null || currentVoxel != null;
				panel.GetOrNull<LabelWidget>("PALETTE_DESC").IsVisible = () => currentSprites != null || currentVoxel != null;
			}

			var colorManager = modData.DefaultRules.Actors[SystemActors.World].TraitInfo<IColorPickerManagerInfo>();

			var colorDropdown = panel.GetOrNull<DropDownButtonWidget>("COLOR");
			if (colorDropdown != null)
			{
				var color = Game.Settings.Player.Color;
				colorDropdown.IsDisabled = () => !colorPickerPalettes.Contains(currentPalette);
				colorDropdown.OnMouseDown = _ => colorManager.ShowColorDropDown(colorDropdown, color, null, worldRenderer, c => color = c);
				colorDropdown.IsVisible = () => currentSprites != null || currentVoxel != null;

				panel.Get<ColorBlockWidget>("COLORBLOCK").GetColor = () => color;
			}

			filenameInput = panel.Get<TextFieldWidget>("FILENAME_INPUT");
			filenameInput.OnTextEdited = ApplyFilter;
			filenameInput.OnEscKey = _ =>
			{
				if (string.IsNullOrEmpty(filenameInput.Text))
					filenameInput.YieldKeyboardFocus();
				else
				{
					filenameInput.Text = "";
					filenameInput.OnTextEdited();
				}

				return true;
			};

			var frameContainer = panel.GetOrNull("FRAME_SELECTOR");
			if (frameContainer != null)
				frameContainer.IsVisible = () => (currentSprites != null && currentSprites.Length > 1) ||
					(isVideoLoaded && player != null && player.Video != null && player.Video.FrameCount > 1) ||
					currentSoundFormat != null;

			frameSlider = panel.GetOrNull<SliderWidget>("FRAME_SLIDER");
			if (frameSlider != null)
			{
				frameSlider.OnChange += x =>
				{
					if (!isVideoLoaded)
						currentFrame = (int)Math.Round(x);
				};

				frameSlider.GetValue = () =>
				{
					if (isVideoLoaded)
						return player.Video.CurrentFrameIndex;

					if (currentSound != null)
						return currentSound.SeekPosition * currentSoundFormat.SampleRate;

					return currentFrame;
				};

				frameSlider.IsDisabled = () => isVideoLoaded || currentSoundFormat != null;
			}

			var frameText = panel.GetOrNull<LabelWidget>("FRAME_COUNT");
			if (frameText != null)
			{
				var soundLength = new CachedTransform<double, string>(p =>
					FluentProvider.GetMessage(LengthInSeconds, "length", Math.Round(p, 3)));

				frameText.GetText = () =>
				{
					if (isVideoLoaded)
						return $"{player.Video.CurrentFrameIndex + 1} / {player.Video.FrameCount}";

					if (currentSoundFormat != null)
						return soundLength.Update(currentSoundFormat.LengthInSeconds);

					return $"{currentFrame} / {currentSprites.Length - 1}";
				};
			}

			var playButton = panel.GetOrNull<ButtonWidget>("BUTTON_PLAY");
			if (playButton != null)
			{
				playButton.OnClick = () =>
				{
					if (isVideoLoaded)
						player.Play();
					else if (currentSoundFormat != null)
					{
						if (currentSound != null)
							Game.Sound.StopSound(currentSound);

						currentSound = Game.Sound.Play(currentSoundFormat, Game.Sound.SoundVolume);
					}
					else
						animateFrames = true;
				};

				playButton.IsVisible = () => isVideoLoaded ? player.Paused : !animateFrames || currentSoundFormat != null;
			}

			var pauseButton = panel.GetOrNull<ButtonWidget>("BUTTON_PAUSE");
			if (pauseButton != null)
			{
				pauseButton.OnClick = () =>
				{
					if (isVideoLoaded)
						player.Pause();
					else
						animateFrames = false;
				};

				pauseButton.IsVisible = () => isVideoLoaded ? !player.Paused : (animateFrames && currentSoundFormat == null);
			}

			var stopButton = panel.GetOrNull<ButtonWidget>("BUTTON_STOP");
			if (stopButton != null)
			{
				stopButton.OnClick = () =>
				{
					if (isVideoLoaded)
						player.Stop();
					else if (currentSound != null)
						Game.Sound.StopSound(currentSound);
					else
					{
						currentFrame = 0;
						animateFrames = false;
					}

					if (frameSlider != null)
						frameSlider.Value = 0;
				};
			}

			var nextButton = panel.GetOrNull<ButtonWidget>("BUTTON_NEXT");
			if (nextButton != null)
			{
				nextButton.OnClick = () =>
				{
					if (!isVideoLoaded)
						nextButton.OnClick = SelectNextFrame;
				};

				nextButton.IsVisible = () => !isVideoLoaded && currentSoundFormat == null;
			}

			var prevButton = panel.GetOrNull<ButtonWidget>("BUTTON_PREV");
			if (prevButton != null)
			{
				prevButton.OnClick = () =>
				{
					if (!isVideoLoaded)
						SelectPreviousFrame();
				};

				prevButton.IsVisible = () => !isVideoLoaded && currentSoundFormat == null;
			}

			var spriteScaleSlider = panel.GetOrNull<SliderWidget>("SPRITE_SCALE_SLIDER");
			if (spriteScaleSlider != null)
			{
				spriteScaleSlider.OnChange += x => spriteScale = x;
				spriteScaleSlider.GetValue = () => spriteScale;
				spriteScaleSlider.IsVisible = () => currentSprites != null;
				panel.GetOrNull<LabelWidget>("SPRITE_SCALE").IsVisible = () => currentSprites != null;
			}

			var voxelContainer = panel.GetOrNull("VOXEL_SELECTOR");
			if (voxelContainer != null)
				voxelContainer.IsVisible = () => currentVoxel != null;

			var rollSlider = panel.GetOrNull<SliderWidget>("ROLL_SLIDER");
			if (rollSlider != null)
			{
				rollSlider.OnChange += x =>
				{
					var roll = (int)x;
					modelOrientation = modelOrientation.WithRoll(new WAngle(roll));
				};

				rollSlider.GetValue = () => modelOrientation.Roll.Angle;
			}

			var pitchSlider = panel.GetOrNull<SliderWidget>("PITCH_SLIDER");
			if (pitchSlider != null)
			{
				pitchSlider.OnChange += x =>
				{
					var pitch = (int)x;
					modelOrientation = modelOrientation.WithPitch(new WAngle(pitch));
				};

				pitchSlider.GetValue = () => modelOrientation.Pitch.Angle;
			}

			var yawSlider = panel.GetOrNull<SliderWidget>("YAW_SLIDER");
			if (yawSlider != null)
			{
				yawSlider.OnChange += x =>
				{
					var yaw = (int)x;
					modelOrientation = modelOrientation.WithYaw(new WAngle(yaw));
				};

				yawSlider.GetValue = () => modelOrientation.Yaw.Angle;
			}

			var modelScaleSlider = panel.GetOrNull<SliderWidget>("MODEL_SCALE_SLIDER");
			if (modelScaleSlider != null)
			{
				modelScaleSlider.OnChange += x => modelScale = x;
				modelScaleSlider.GetValue = () => modelScale;
				modelScaleSlider.IsVisible = () => currentVoxel != null;
				panel.GetOrNull<LabelWidget>("MODEL_SCALE").IsVisible = () => currentVoxel != null;
			}

			var assetBrowserModData = modData.Manifest.Get<AssetBrowser>();
			allowedSpriteExtensions = assetBrowserModData.SpriteExtensions.Select(x => x.ToLowerInvariant()).ToArray();
			allowedModelExtensions = assetBrowserModData.ModelExtensions.Select(x => x.ToLowerInvariant()).ToArray();
			allowedAudioExtensions = assetBrowserModData.AudioExtensions.Select(x => x.ToLowerInvariant()).ToArray();
			allowedVideoExtensions = assetBrowserModData.VideoExtensions.Select(x => x.ToLowerInvariant()).ToArray();
			allowedExtensions = allowedSpriteExtensions
				.Union(allowedModelExtensions)
				.Union(allowedAudioExtensions)
				.Union(allowedVideoExtensions)
				.ToArray();

			acceptablePackages = modData.ModFiles.MountedPackages.Where(p =>
				p.Contents.Any(c => allowedExtensions.Contains(Path.GetExtension(c).ToLowerInvariant())));

			assetList = panel.Get<ScrollPanelWidget>("ASSET_LIST");
			template = panel.Get<ScrollItemWidget>("ASSET_TEMPLATE");
			PopulateAssetList();

			var closeButton = panel.GetOrNull<ButtonWidget>("CLOSE_BUTTON");
			if (closeButton != null)
				closeButton.OnClick = () =>
				{
					ClearLoadedAssets();
					Ui.CloseWindow();
					onExit();
				};
		}

		void SelectNextFrame()
		{
			currentFrame++;
			if (currentFrame >= currentSprites.Length)
				currentFrame = 0;
		}

		void SelectPreviousFrame()
		{
			currentFrame--;
			if (currentFrame < 0)
				currentFrame = currentSprites.Length - 1;
		}

		readonly Dictionary<string, bool> assetVisByName = [];

		bool FilterAsset(string filename)
		{
			var filter = filenameInput.Text;

			if (string.IsNullOrWhiteSpace(filter))
				return true;

			if (filename.Contains(filter, StringComparison.OrdinalIgnoreCase))
				return true;

			return false;
		}

		void ApplyFilter()
		{
			assetVisByName.Clear();
			assetList.Layout.AdjustChildren();
			assetList.ScrollToTop();

			// Select the first visible
			var firstVisible = assetVisByName.FirstOrDefault(kvp => kvp.Value);

			if (firstVisible.Key != null && modData.DefaultFileSystem.TryGetPackageContaining(firstVisible.Key, out var package, out var filename))
				LoadAsset(package, filename);
		}

		void AddAsset(ScrollPanelWidget list, string filepath, IReadOnlyPackage package, ScrollItemWidget template)
		{
			var item = ScrollItemWidget.Setup(template,
				() => currentFilename == filepath && currentPackage == package,
				() => LoadAsset(package, filepath));

			var label = item.Get<LabelWithTooltipWidget>("TITLE");
			WidgetUtils.TruncateLabelToTooltip(label, filepath);
			label.GetTooltipText = () => $"{filepath}\n{package.Name}";

			item.IsVisible = () =>
			{
				var allowed =
					(assetTypesToDisplay.HasFlag(AssetType.Sprite)
						&& allowedSpriteExtensions.Any(ext => filepath.EndsWith(ext, true, CultureInfo.InvariantCulture)))
					|| (assetTypesToDisplay.HasFlag(AssetType.Model)
						&& allowedModelExtensions.Any(ext => filepath.EndsWith(ext, true, CultureInfo.InvariantCulture)))
					|| (assetTypesToDisplay.HasFlag(AssetType.Audio)
						&& allowedAudioExtensions.Any(ext => filepath.EndsWith(ext, true, CultureInfo.InvariantCulture)))
					|| (assetTypesToDisplay.HasFlag(AssetType.Video)
						&& allowedVideoExtensions.Any(ext => filepath.EndsWith(ext, true, CultureInfo.InvariantCulture)))
					|| (assetTypesToDisplay.HasFlag(AssetType.Unknown)
						&& !allowedExtensions.Any(ext => filepath.EndsWith(ext, true, CultureInfo.InvariantCulture)));

				if (assetVisByName.TryGetValue(filepath, out var visible))
					return visible && allowed;

				visible = FilterAsset(filepath);
				assetVisByName.Add(filepath, visible);
				return visible && allowed;
			};

			list.AddChild(item);
		}

		bool LoadAsset(IReadOnlyPackage package, string filename)
		{
			ClearLoadedAssets();

			if (string.IsNullOrEmpty(filename))
				return false;

			if (!package.Contains(filename))
				return false;

			isLoadError = false;

			try
			{
				currentPackage = package;
				currentFilename = filename;
				var prefix = "";

				if (modData.DefaultFileSystem is OpenRA.FileSystem.FileSystem fs)
				{
					prefix = fs.GetPrefix(package);
					if (prefix != null)
						prefix += "|";
				}

				var fileExtension = Path.GetExtension(filename.ToLowerInvariant());
				if (allowedSpriteExtensions.Contains(fileExtension))
				{
					currentSprites = spriteCache[prefix + filename];
					currentFrame = 0;

					if (frameSlider != null && currentSprites?.Length > 0)
					{
						frameSlider.MaximumValue = (float)currentSprites.Length - 1;
						frameSlider.Ticks = currentSprites.Length;
					}

					currentVoxel = null;
				}
				else if (allowedModelExtensions.Contains(fileExtension))
				{
					var voxelName = Path.GetFileNameWithoutExtension(filename);
					currentVoxel = world.WorldActor.Trait<IModelCache>().GetModel(prefix + voxelName);
					currentSprites = null;
				}
				else if (allowedAudioExtensions.Contains(fileExtension))
				{
					// Mute music so it doesn't interfere with the current asset.
					MuteSounds();

					currentAudioStream = Game.ModData.DefaultFileSystem.Open(prefix + filename);
					foreach (var modDataSoundLoader in Game.ModData.SoundLoaders)
					{
						if (modDataSoundLoader.TryParseSound(currentAudioStream, out currentSoundFormat))
						{
							if (frameSlider != null)
							{
								frameSlider.MaximumValue = currentSoundFormat.LengthInSeconds * currentSoundFormat.SampleRate;
								frameSlider.Ticks = 0;
							}

							break;
						}
					}
				}
				else if (allowedVideoExtensions.Contains(fileExtension))
				{
					// Mute music so it doesn't interfere with the current asset.
					MuteSounds();

					var video = VideoLoader.GetVideo(Game.ModData.DefaultFileSystem.Open(prefix + filename), true, Game.ModData.VideoLoaders);
					if (video != null)
					{
						player = panel.Get<VideoPlayerWidget>("PLAYER");
						player.LoadAndPlay(prefix + filename);
						player.DrawOverlay = false;
						isVideoLoaded = true;

						if (frameSlider != null)
						{
							frameSlider.MaximumValue = (float)player.Video.FrameCount - 1;
							frameSlider.Ticks = 0;
						}
					}
				}
				else
					return false;
			}
			catch (Exception exception)
			{
				isLoadError = true;
				Log.AddChannel("assetbrowser", "assetbrowser.log");
				Log.Write("assetbrowser", $"Error reading {filename}");
				Log.Write("assetbrowser", exception);

				return false;
			}

			return true;
		}

		bool ShowSourceDropdown(DropDownButtonWidget dropdown)
		{
			var sourceName = new CachedTransform<IReadOnlyPackage, string>(GetSourceDisplayName);
			ScrollItemWidget SetupItem(IReadOnlyPackage source, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => assetSource == source,
					() => { assetSource = source; PopulateAssetList(); });

				item.Get<LabelWidget>("LABEL").GetText = () => sourceName.Update(source);
				return item;
			}

			var sources = new[] { (IReadOnlyPackage)null }.Concat(acceptablePackages);
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 280, sources, SetupItem);
			return true;
		}

		void PopulateAssetList()
		{
			assetList.RemoveChildren();

			var files = new SortedList<string, List<IReadOnlyPackage>>();

			if (assetSource != null)
				foreach (var content in assetSource.Contents)
					files.Add(content, [assetSource]);
			else
			{
				foreach (var mountedPackage in modData.ModFiles.MountedPackages)
				{
					foreach (var content in mountedPackage.Contents)
					{
						if (!files.TryGetValue(content, out var list))
							files.Add(content, [mountedPackage]);
						else
							list.Add(mountedPackage);
					}
				}
			}

			foreach (var file in files)
			{
				foreach (var package in file.Value)
				{
					// Don't show unknown files in the engine dir - it is full of code and git and IDE files.
					// But do show supported types that are inside just in case.
					// Also don't show "files" without extensions because those may be folders.
					var fileExtension = Path.GetExtension(file.Key.ToLowerInvariant());
					if (string.IsNullOrWhiteSpace(fileExtension) || (package.Name == Platform.EngineDir && !allowedExtensions.Contains(fileExtension)))
						continue;

					AddAsset(assetList, file.Key, package, template);
				}
			}
		}

		bool ShowPaletteDropdown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(string name, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => currentPalette == name,
					() => currentPalette = name);

				item.Get<LabelWidget>("LABEL").GetText = () => name;
				return item;
			}

			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 280, palettes, SetupItem);
			return true;
		}

		string GetSourceDisplayName(IReadOnlyPackage source)
		{
			if (source == null)
				return allPackages;

			// Packages that are explicitly mounted in the filesystem use their explicit mount name
			var fs = (OpenRA.FileSystem.FileSystem)modData.DefaultFileSystem;
			var name = fs.GetPrefix(source);

			// Fall back to the path relative to the mod, engine, or support dir
			if (name == null)
			{
				name = source.Name;
				var compare = Platform.CurrentPlatform == PlatformType.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				if (name.StartsWith(modData.Manifest.Package.Name, compare))
					name = "$" + modData.Manifest.Id + "/" + name[(modData.Manifest.Package.Name.Length + 1)..];
				else if (name.StartsWith(Platform.EngineDir, compare))
					name = "./" + name[Platform.EngineDir.Length..];
				else if (name.StartsWith(Platform.SupportDir, compare))
					name = "^" + name[Platform.SupportDir.Length..];
			}

			if (name.Length > 18)
				name = "..." + name[^15..];

			return name;
		}

		// Mute/UnMute code copied from MissionBrowserLogic.
		float cachedMusicVolume;
		void MuteSounds()
		{
			if (Game.Sound.MusicVolume > 0)
			{
				cachedMusicVolume = Game.Sound.MusicVolume;
				Game.Sound.MusicVolume = 0;
			}
		}

		void UnMuteSounds()
		{
			if (cachedMusicVolume > 0)
				Game.Sound.MusicVolume = cachedMusicVolume;
		}

		void ClearLoadedAssets()
		{
			if (currentSound != null)
				Game.Sound.StopSound(currentSound);

			currentSprites = null;
			currentFrame = 0;

			currentVoxel = null;

			currentSound = null;
			currentSoundFormat = null;
			currentAudioStream?.Dispose();
			currentAudioStream = null;

			player?.Stop();
			player = null;
			isVideoLoaded = false;

			// Just in case we're switching away from a type of asset that forced the music to mute.
			UnMuteSounds();
		}

		Widget CreateAssetTypesPanel()
		{
			var assetTypesPanel = Ui.LoadWidget("ASSET_TYPES_PANEL", null, []);
			var assetTypeTemplate = assetTypesPanel.Get<CheckboxWidget>("ASSET_TYPE_TEMPLATE");

			var allAssetTypes = new[] { AssetType.Sprite, AssetType.Model, AssetType.Audio, AssetType.Video, AssetType.Unknown };
			foreach (var type in allAssetTypes)
			{
				var assetType = assetTypeTemplate.Clone();
				var text = type.ToString();
				assetType.GetText = () => text;
				assetType.IsChecked = () => assetTypesToDisplay.HasFlag(type);
				assetType.IsVisible = () => true;
				assetType.OnClick = () =>
				{
					assetTypesToDisplay ^= type;
					PopulateAssetList();
				};

				assetTypesPanel.AddChild(assetType);
			}

			return assetTypesPanel;
		}
	}
}
