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
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using OpenRA.Activities;
using OpenRA.FileSystem;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA.Traits
{
	[AttributeUsage(AttributeTargets.Interface)]
	public sealed class RequireExplicitImplementationAttribute : Attribute { }

	[Flags]
	public enum DamageState
	{
		Undamaged = 1,
		Light = 2,
		Medium = 4,
		Heavy = 8,
		Critical = 16,
		Dead = 32
	}

	/// <summary>
	/// Type tag for DamageTypes <see cref="BitSet{T}"/>.
	/// </summary>
	public sealed class DamageType { DamageType() { } }

	public interface IHealthInfo : ITraitInfoInterface
	{
		int MaxHP { get; }
	}

	public interface IHealth
	{
		DamageState DamageState { get; }
		int HP { get; }
		int MaxHP { get; }
		int DisplayHP { get; }
		bool IsDead { get; }

		void InflictDamage(Actor self, Actor attacker, Damage damage, bool ignoreModifiers);
		void Kill(Actor self, Actor attacker, BitSet<DamageType> damageTypes);
	}

	[Flags]
	public enum PlayerRelationship
	{
		None = 0,
		Enemy = 1,
		Neutral = 2,
		Ally = 4,
	}

	public static class PlayerRelationshipExts
	{
		public static bool HasRelationship(this PlayerRelationship r, PlayerRelationship relationship)
		{
			// PERF: Enum.HasFlag is slower and requires allocations.
			return (r & relationship) == relationship;
		}
	}

	public class AttackInfo
	{
		public Damage Damage;
		public Actor Attacker;
		public DamageState DamageState;
		public DamageState PreviousDamageState;
	}

	public class Damage
	{
		public readonly int Value;
		public readonly BitSet<DamageType> DamageTypes;

		public Damage(int damage, BitSet<DamageType> damageTypes)
		{
			Value = damage;
			DamageTypes = damageTypes;
		}

		public Damage(int damage)
		{
			Value = damage;
			DamageTypes = default;
		}
	}

	[RequireExplicitImplementation]
	public interface ITick { void Tick(Actor self); }
	[RequireExplicitImplementation]
	public interface ITickRender { void TickRender(WorldRenderer wr, Actor self); }
	public interface IRender
	{
		IEnumerable<IRenderable> Render(Actor self, WorldRenderer wr);
		IEnumerable<Rectangle> ScreenBounds(Actor self, WorldRenderer wr);
	}

	public interface IMouseBounds { Polygon MouseoverBounds(Actor self, WorldRenderer wr); }
	public interface IMouseBoundsInfo : ITraitInfoInterface { }
	public interface IAutoMouseBounds { Rectangle AutoMouseoverBounds(Actor self, WorldRenderer wr); }

	public interface IIssueOrder
	{
		IEnumerable<IOrderTargeter> Orders { get; }
		Order IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued);
	}

	[Flags]
	public enum TargetModifiers { None = 0, ForceAttack = 1, ForceQueue = 2, ForceMove = 4 }

	public static class TargetModifiersExts
	{
		public static bool HasModifier(this TargetModifiers self, TargetModifiers m)
		{
			// PERF: Enum.HasFlag is slower and requires allocations.
			return (self & m) == m;
		}
	}

	public interface IOrderTargeter
	{
		string OrderID { get; }
		int OrderPriority { get; }
		bool CanTarget(Actor self, in Target target, ref TargetModifiers modifiers, ref string cursor);
		bool IsQueued { get; }
		bool TargetOverridesSelection(Actor self, in Target target, List<Actor> actorsAt, CPos xy, TargetModifiers modifiers);
	}

	public interface IResolveOrder { void ResolveOrder(Actor self, Order order); }
	public interface IValidateOrder { bool OrderValidation(OrderManager orderManager, World world, int clientId, Order order); }
	public interface IOrderVoice { string VoicePhraseForOrder(Actor self, Order order); }

	[RequireExplicitImplementation]
	public interface INotifyCreated { void Created(Actor self); }

	[RequireExplicitImplementation]
	public interface INotifyAddedToWorld { void AddedToWorld(Actor self); }
	[RequireExplicitImplementation]
	public interface INotifyRemovedFromWorld { void RemovedFromWorld(Actor self); }

	[RequireExplicitImplementation]
	public interface INotifyActorDisposing { void Disposing(Actor self); }
	public interface INotifyOwnerChanged { void OnOwnerChanged(Actor self, Player oldOwner, Player newOwner); }
	public interface INotifyEffectiveOwnerChanged { void OnEffectiveOwnerChanged(Actor self, Player oldEffectiveOwner, Player newEffectiveOwner); }
	public interface INotifyOwnerLost { void OnOwnerLost(Actor self); }

	[RequireExplicitImplementation]
	public interface IVoiced
	{
		string VoiceSet { get; }
		bool PlayVoice(Actor self, string phrase, string variant);
		bool PlayVoiceLocal(Actor self, string phrase, string variant, float volume);
		bool HasVoice(Actor self, string voice);
	}

	[RequireExplicitImplementation]
	public interface IStoresResourcesInfo : ITraitInfoInterface
	{
		string[] ResourceTypes { get; }
	}

	public interface IStoresResources
	{
		bool HasType(string resourceType);

		/// <summary>The amount of resources that can be stored.</summary>
		int Capacity { get; }

		/// <summary>Stored resources.</summary>
		/// <remarks>Dictionary key refers to resourceType, value refers to resource amount.</remarks>
		IReadOnlyDictionary<string, int> Contents { get; }

		/// <summary>A performance cheap method of getting the total sum of contents.</summary>
		int ContentsSum { get; }

		/// <summary>Returns the amount of <paramref name="value"/> that was not added.</summary>
		int AddResource(string resourceType, int value);

		/// <summary>Returns the amount of <paramref name="value"/> that was not removed.</summary>
		int RemoveResource(string resourceType, int value);
	}

	public interface IEffectiveOwner
	{
		bool Disguised { get; }
		Player Owner { get; }
	}

	public interface ITooltip
	{
		ITooltipInfo TooltipInfo { get; }
		Player Owner { get; }
	}

	public interface ITooltipInfo : ITraitInfoInterface
	{
		string TooltipForPlayerStance(PlayerRelationship stance);
		bool IsOwnerRowVisible { get; }
	}

	public interface IProvideTooltipInfo
	{
		bool IsTooltipVisible(Player forPlayer);
		string TooltipText { get; }
	}

	public interface IDisabledTrait { bool IsTraitDisabled { get; } }

	public interface IDefaultVisibilityInfo : ITraitInfoInterface { }
	public interface IDefaultVisibility { bool IsVisible(Actor self, Player byPlayer); }
	public interface IVisibilityModifier { bool IsVisible(Actor self, Player byPlayer); }

	public interface IActorMap
	{
		IEnumerable<Actor> GetActorsAt(CPos a);
		IEnumerable<Actor> GetActorsAt(CPos a, SubCell sub);
		bool HasFreeSubCell(CPos cell, bool checkTransient = true);
		SubCell FreeSubCell(CPos cell, SubCell preferredSubCell = SubCell.Any, bool checkTransient = true);
		SubCell FreeSubCell(CPos cell, SubCell preferredSubCell, Func<Actor, bool> checkIfBlocker);
		bool AnyActorsAt(CPos a);
		bool AnyActorsAt(CPos a, SubCell sub, bool checkTransient = true);
		bool AnyActorsAt(CPos a, SubCell sub, Func<Actor, bool> withCondition);
		IEnumerable<Actor> AllActors();
		void AddInfluence(Actor self, IOccupySpace ios);
		void RemoveInfluence(Actor self, IOccupySpace ios);
		int AddCellTrigger(CPos[] cells, Action<Actor> onEntry, Action<Actor> onExit);
		IEnumerable<CPos> TriggerPositions();
		void RemoveCellTrigger(int id);
		int AddProximityTrigger(WPos pos, WDist range, WDist vRange, Action<Actor> onEntry, Action<Actor> onExit);
		void RemoveProximityTrigger(int id);
		void UpdateProximityTrigger(int id, WPos newPos, WDist newRange, WDist newVRange);
		void AddPosition(Actor a, IOccupySpace ios);
		void RemovePosition(Actor a, IOccupySpace ios);
		void UpdatePosition(Actor a, IOccupySpace ios);
		IEnumerable<Actor> ActorsInBox(WPos a, WPos b);

		WDist LargestActorRadius { get; }
		WDist LargestBlockingActorRadius { get; }

		void UpdateOccupiedCells(IOccupySpace ios);
		event Action<CPos> CellUpdated;
	}

	[RequireExplicitImplementation]
	public interface IRenderModifier
	{
		IEnumerable<IRenderable> ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r);

		// HACK: This is here to support the WithShadow trait.
		// That trait should be rewritten using standard techniques, and then this interface method removed
		IEnumerable<Rectangle> ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> r);
	}

	[RequireExplicitImplementation]
	public interface ITilesetSpecificPaletteInfo : ITraitInfoInterface
	{
		string Tileset { get; }
	}

	[RequireExplicitImplementation]
	public interface IProvidesCursorPaletteInfo : ITraitInfoInterface
	{
		string Palette { get; }
		ImmutablePalette ReadPalette(IReadOnlyFileSystem fileSystem);
	}

	public interface ILoadsPalettes { void LoadPalettes(WorldRenderer wr); }
	public interface ILoadsPlayerPalettes { void LoadPlayerPalettes(WorldRenderer wr, string playerName, Color playerColor, bool replaceExisting); }
	public interface IPaletteModifier { void AdjustPalette(IReadOnlyDictionary<string, MutablePalette> b); }

	[RequireExplicitImplementation]
	public interface ISelectionBar { float GetValue(); Color GetColor(); bool DisplayWhenEmpty { get; } }

	public interface ISelectionDecorations
	{
		IEnumerable<IRenderable> RenderSelectionAnnotations(Actor self, WorldRenderer worldRenderer, Color color);
		int2 GetDecorationOrigin(Actor self, WorldRenderer wr, string pos, int2 margin);
	}

	public interface IEditorSelectionLayer : ITraitInfoInterface { }
	public interface IEditorPasteLayer : ITraitInfoInterface { }

	public interface IMapPreviewSignatureInfo : ITraitInfoInterface
	{
		void PopulateMapPreviewSignatureCells(Map map, ActorInfo ai, ActorReference s, List<(MPos Uv, Color Color)> destinationBuffer);
	}

	public interface IOccupySpaceInfo : ITraitInfoInterface
	{
		IReadOnlyDictionary<CPos, SubCell> OccupiedCells(ActorInfo info, CPos location, SubCell subCell = SubCell.Any);
		bool SharesCell { get; }
	}

	public interface IOccupySpace
	{
		WPos CenterPosition { get; }
		CPos TopLeft { get; }
		(CPos Cell, SubCell SubCell)[] OccupiedCells();
	}

	public enum SubCell : byte { Invalid = byte.MaxValue, Any = byte.MaxValue - 1, FullCell = 0, First = 1 }

	public interface ITemporaryBlockerInfo : ITraitInfoInterface { }

	[RequireExplicitImplementation]
	public interface ITemporaryBlocker
	{
		bool CanRemoveBlockage(Actor self, Actor blocking);
		bool IsBlocking(Actor self, CPos cell);
	}

	public interface IFacing
	{
		WAngle TurnSpeed { get; }
		WAngle Facing { get; set; }
		WRot Orientation { get; }
	}

	public interface IFacingInfo : ITraitInfoInterface { WAngle GetInitialFacing(); }

	public interface ITraitInfoInterface { }

	public abstract class TraitInfo : ITraitInfoInterface
	{
		// Value is set using reflection during TraitInfo creation
		[FieldLoader.Ignore]
		public readonly string InstanceName = null;

		public abstract object Create(ActorInitializer init);
	}

	public class TraitInfo<T> : TraitInfo where T : new()
	{
		public override object Create(ActorInitializer init) { return new T(); }
	}

	public interface ILobbyCustomRulesIgnore { }

	[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Not a real interface, but more like a tag.")]
	public interface Requires<T> where T : class, ITraitInfoInterface { }

	[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Not a real interface, but more like a tag.")]
	public interface NotBefore<T> where T : class, ITraitInfoInterface { }

	public interface IActivityInterface { }

	[RequireExplicitImplementation]
	public interface INotifySelected { void Selected(Actor self); }
	[RequireExplicitImplementation]
	public interface INotifySelection { void SelectionChanged(); }

	public interface IWorldLoaded { void WorldLoaded(World w, WorldRenderer wr); }
	public interface IPostWorldLoaded { void PostWorldLoaded(World w, WorldRenderer wr); }
	public interface INotifyGameLoading { void GameLoading(World w); }
	public interface INotifyGameLoaded { void GameLoaded(World w); }
	public interface INotifyGameSaved { void GameSaved(World w); }

	public interface IGameSaveTraitData
	{
		List<MiniYamlNode> IssueTraitData(Actor self);
		void ResolveTraitData(Actor self, MiniYaml data);
	}

	[RequireExplicitImplementation]
	public interface ICreatePlayers { void CreatePlayers(World w, MersenneTwister playerRandom); }

	[RequireExplicitImplementation]
	public interface ICreatePlayersInfo : ITraitInfoInterface
	{
		void CreateServerPlayers(MapPreview map, Session lobbyInfo, List<GameInformation.Player> players, MersenneTwister playerRandom);
	}

	[RequireExplicitImplementation]
	public interface IAssignSpawnPoints
	{
		CPos AssignHomeLocation(World world, Session.Client client, MersenneTwister playerRandom);
		int SpawnPointForPlayer(Player player);
	}

	[RequireExplicitImplementation]
	public interface IAssignSpawnPointsInfo : ITraitInfoInterface
	{
		object InitializeState(MapPreview map, Session lobbyInfo);
		int AssignSpawnPoint(object state, Session lobbyInfo, Session.Client client, MersenneTwister playerRandom);
	}

	public interface IBotInfo : ITraitInfoInterface
	{
		string Type { get; }
		string Name { get; }
	}

	public interface IBot
	{
		void Activate(Player p);
		void QueueOrder(Order order);
		IBotInfo Info { get; }
		Player Player { get; }
	}

	[RequireExplicitImplementation]
	public interface IRenderOverlay { void Render(WorldRenderer wr); }

	[RequireExplicitImplementation]
	public interface INotifyBecomingIdle { void OnBecomingIdle(Actor self); }

	[RequireExplicitImplementation]
	public interface INotifyIdle { void TickIdle(Actor self); }

	public interface IRenderAboveWorld { void RenderAboveWorld(Actor self, WorldRenderer wr); }
	public interface IRenderShroud { void RenderShroud(WorldRenderer wr); }

	[RequireExplicitImplementation]
	public interface IRenderTerrain { void RenderTerrain(WorldRenderer wr, Viewport viewport); }

	[RequireExplicitImplementation]
	public interface ITerrainLighting
	{
		event Action<MPos> CellChanged;
		float3 TintAt(WPos pos);
	}

	public interface IRenderAboveShroud
	{
		IEnumerable<IRenderable> RenderAboveShroud(Actor self, WorldRenderer wr);
		bool SpatiallyPartitionable { get; }
	}

	public interface IRenderAboveShroudWhenSelected
	{
		IEnumerable<IRenderable> RenderAboveShroud(Actor self, WorldRenderer wr);
		bool SpatiallyPartitionable { get; }
	}

	public interface IRenderAnnotations
	{
		IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr);
		bool SpatiallyPartitionable { get; }
	}

	public interface IRenderAnnotationsWhenSelected
	{
		IEnumerable<IRenderable> RenderAnnotations(Actor self, WorldRenderer wr);
		bool SpatiallyPartitionable { get; }
	}

	public enum PostProcessPassType { AfterShroud, AfterWorld, AfterActors }

	[RequireExplicitImplementation]
	public interface IRenderPostProcessPass
	{
		PostProcessPassType Type { get; }
		bool Enabled { get; }
		void Draw(WorldRenderer wr);
	}

	[Flags]
	public enum SelectionPriorityModifiers
	{
		None = 0,
		Ctrl = 1,
		Alt = 2
	}

	[RequireExplicitImplementation]
	public interface ISelectableInfo : ITraitInfoInterface
	{
		int Priority { get; }
		SelectionPriorityModifiers PriorityModifiers { get; }
		string Voice { get; }
	}

	public interface ISelection
	{
		int Hash { get; }
		IReadOnlyCollection<Actor> Actors { get; }

		void Add(Actor a);
		void Remove(Actor a);
		bool Contains(Actor a);
		void Combine(World world, IEnumerable<Actor> newSelection, bool isCombine, bool isClick);
		void Clear();
		bool RolloverContains(Actor a);
		void SetRollover(IEnumerable<Actor> actors);
	}

	public interface IControlGroupsInfo : ITraitInfoInterface
	{
		string[] Groups { get; }
	}

	public interface IControlGroups
	{
		string[] Groups { get; }

		void SelectControlGroup(int group);
		void CreateControlGroup(int group);
		void AddSelectionToControlGroup(int group);
		void CombineSelectionWithControlGroup(int group);
		void AddToControlGroup(Actor a, int group);
		void RemoveFromControlGroup(Actor a);
		int? GetControlGroupForActor(Actor a);
		IEnumerable<Actor> GetActorsInControlGroup(int group);
	}

	/// <summary>
	/// Indicates target types as defined on <see cref="ITargetable"/> are present in a <see cref="BitSet{T}"/>.
	/// </summary>
	public sealed class TargetableType { TargetableType() { } }

	public interface ITargetableInfo : ITraitInfoInterface
	{
		BitSet<TargetableType> GetTargetTypes();
	}

	public interface ITargetable
	{
		// Check IsTraitEnabled or !IsTraitDisabled first
		BitSet<TargetableType> TargetTypes { get; }
		bool TargetableBy(Actor self, Actor byActor);
		bool RequiresForceFire { get; }
	}

	[RequireExplicitImplementation]
	public interface ITargetablePositions
	{
		IEnumerable<WPos> TargetablePositions(Actor self);
	}

	public interface IMoveInfo : ITraitInfoInterface
	{
		Color GetTargetLineColor();
	}

	[RequireExplicitImplementation]
	public interface IGameOver { void GameOver(World world); }

	public interface IWarhead
	{
		int Delay { get; }
		bool IsValidAgainst(Actor victim, Actor firedBy);
		bool IsValidAgainst(FrozenActor victim, Actor firedBy);
		void DoImpact(in Target target, WarheadArgs args);
	}

	public interface IRulesetLoaded<TInfo> { void RulesetLoaded(Ruleset rules, TInfo info); }
	public interface IRulesetLoaded : IRulesetLoaded<ActorInfo>, ITraitInfoInterface { }

	[RequireExplicitImplementation]
	public interface ILobbyOptions : ITraitInfoInterface
	{
		IEnumerable<LobbyOption> LobbyOptions(MapPreview map);
	}

	public class LobbyOption
	{
		public readonly string Id;
		public readonly string Name;
		public readonly string Description;
		public readonly IReadOnlyDictionary<string, string> Values;
		public readonly string DefaultValue;
		public readonly bool IsLocked;
		public readonly bool IsVisible;
		public readonly int DisplayOrder;

		public LobbyOption(MapPreview map, string id, string name, string description, bool visible, int displayorder,
			IReadOnlyDictionary<string, string> values, string defaultValue, bool locked)
		{
			Id = id;
			Name = map.GetMessage(name);
			Description = description != null ? map.GetMessage(description).Replace(@"\n", "\n") : null;
			IsVisible = visible;
			DisplayOrder = displayorder;
			Values = values.ToDictionary(v => v.Key, v => map.GetMessage(v.Value));
			DefaultValue = defaultValue;
			IsLocked = locked;
		}

		public virtual string Label(string value)
		{
			return Values[value];
		}
	}

	public class LobbyBooleanOption : LobbyOption
	{
		static readonly Dictionary<string, string> BoolValues = new()
		{
			{ true.ToString(), "Enabled" },
			{ false.ToString(), "Disabled" }
		};

		public LobbyBooleanOption(MapPreview map, string id, string name, string description, bool visible, int displayorder, bool defaultValue, bool locked)
			: base(map, id, name, description, visible, displayorder, new ReadOnlyDictionary<string, string>(BoolValues), defaultValue.ToString(), locked) { }

		public override string Label(string newValue)
		{
			return BoolValues[newValue].ToLowerInvariant();
		}
	}

	[RequireExplicitImplementation]
	public interface IUnlocksRenderPlayer { bool RenderPlayerUnlocked { get; } }

	[RequireExplicitImplementation]
	public interface ICreationActivity { Activity GetCreationActivity(); }

	[RequireExplicitImplementation]
	public interface IObservesVariablesInfo : ITraitInfoInterface { }

	public delegate void VariableObserverNotifier(Actor self, IReadOnlyDictionary<string, int> variables);
	public readonly record struct VariableObserver(VariableObserverNotifier Notifier, IEnumerable<string> Variables);

	public interface IObservesVariables
	{
		IEnumerable<VariableObserver> GetVariableObservers();
	}

	[RequireExplicitImplementation]
	public interface INotifyPlayerDisconnected
	{
		void PlayerDisconnected(Actor self, Player p);
	}

	// Type tag for crush class bits
	public class CrushClass { }

	[RequireExplicitImplementation]
	public interface ICrushable
	{
		bool CrushableBy(Actor self, Actor crusher, BitSet<CrushClass> crushClasses);
		LongBitSet<PlayerBitMask> CrushableBy(Actor self, BitSet<CrushClass> crushClasses);
	}
}
