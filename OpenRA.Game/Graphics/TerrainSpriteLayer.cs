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
using System.IO;
using System.Runtime.CompilerServices;

namespace OpenRA.Graphics
{
	public sealed class TerrainSpriteLayer : IDisposable
	{
		// PERF: we can reuse the IndexBuffer as all layers have the same size.
		static readonly ConditionalWeakTable<World, IndexBufferRc> IndexBuffers = [];
		readonly IndexBufferRc indexBufferWrapper;

		public readonly BlendMode BlendMode;

		readonly Sheet[] sheets;
		readonly Sprite emptySprite;

		readonly IVertexBuffer<Vertex> vertexBuffer;
		readonly Vertex[] vertices;
		readonly bool[] ignoreTint;
		readonly HashSet<int> dirtyRows = [];
		readonly int indexRowStride;
		readonly int vertexRowStride;
		readonly bool restrictToBounds;

		readonly WorldRenderer worldRenderer;
		readonly Map map;

		readonly PaletteReference[] palettes;

		public TerrainSpriteLayer(World world, WorldRenderer wr, Sprite emptySprite, BlendMode blendMode, bool restrictToBounds)
		{
			worldRenderer = wr;
			this.restrictToBounds = restrictToBounds;
			this.emptySprite = emptySprite;
			sheets = new Sheet[SpriteRenderer.SheetCount];
			BlendMode = blendMode;
			map = world.Map;

			vertexRowStride = 4 * map.MapSize.X;
			vertices = new Vertex[vertexRowStride * map.MapSize.Y];
			vertexBuffer = Game.Renderer.Context.CreateVertexBuffer<Vertex>(vertices.Length);

			indexRowStride = 6 * map.MapSize.X;
			lock (IndexBuffers)
			{
				indexBufferWrapper = IndexBuffers.GetValue(world, world => new IndexBufferRc(world));
				indexBufferWrapper.AddRef();
			}

			palettes = new PaletteReference[map.MapSize.X * map.MapSize.Y];
			wr.PaletteInvalidated += UpdatePaletteIndices;

			if (wr.TerrainLighting != null)
			{
				ignoreTint = new bool[vertexRowStride * map.MapSize.Y];
				wr.TerrainLighting.CellChanged += UpdateTint;
			}
		}

		void UpdatePaletteIndices()
		{
			for (var i = 0; i < vertices.Length; i++)
			{
				var v = vertices[i];
				var p = palettes[i / 4]?.TextureIndex ?? 0;
				var c = (uint)((p & 0xFFFF) << 16) | (v.C & 0xFFFF);
				vertices[i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, c, v.R, v.G, v.B, v.A);
			}

			for (var row = 0; row < map.MapSize.Y; row++)
				dirtyRows.Add(row);
		}

		public void Clear(CPos cell)
		{
			Update(cell, null, null, 1f, 1f, true);
		}

		public void Update(CPos cell, ISpriteSequence sequence, PaletteReference palette, int frame)
		{
			Update(cell, sequence.GetSprite(frame), palette, sequence.Scale, sequence.GetAlpha(frame), sequence.IgnoreWorldTint);
		}

		public void Update(CPos cell, Sprite sprite, PaletteReference palette, float scale = 1f, float alpha = 1f, bool ignoreTint = false)
		{
			var xyz = float3.Zero;
			if (sprite != null)
			{
				var cellOrigin = map.CenterOfCell(cell) - new WVec(0, 0, map.Grid.Ramps[map.Ramp[cell]].CenterHeightOffset);
				xyz = worldRenderer.Screen3DPosition(cellOrigin) + scale * (sprite.Offset - 0.5f * sprite.Size);
			}

			Update(cell.ToMPos(map.Grid.Type), sprite, palette, xyz, scale, alpha, ignoreTint);
		}

		void UpdateTint(MPos uv)
		{
			var offset = vertexRowStride * uv.V + 4 * uv.U;
			if (ignoreTint[offset])
			{
				for (var i = 0; i < 4; i++)
				{
					var v = vertices[offset + i];
					vertices[offset + i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, v.C, v.A * float3.Ones, v.A);
				}

				return;
			}

			// Allow the terrain tint to vary linearly across the cell to smooth out the staircase effect
			// This is done by sampling the lighting the corners of the sprite, even though those pixels are
			// transparent for isometric tiles
			var tl = worldRenderer.TerrainLighting;
			var pos = map.CenterOfCell(uv.ToCPos(map));
			var step = map.Grid.TileScale / 2;
			var weights = new[]
			{
				tl.TintAt(pos + new WVec(-step, -step, 0)),
				tl.TintAt(pos + new WVec(step, -step, 0)),
				tl.TintAt(pos + new WVec(step, step, 0)),
				tl.TintAt(pos + new WVec(-step, step, 0))
			};

			// Apply tint directly to the underlying vertices
			// This saves us from having to re-query the sprite information, which has not changed
			for (var i = 0; i < 4; i++)
			{
				var v = vertices[offset + i];
				vertices[offset + i] = new Vertex(v.X, v.Y, v.Z, v.S, v.T, v.U, v.V, v.C, v.A * weights[i], v.A);
			}

			dirtyRows.Add(uv.V);
		}

		int GetOrAddSheetIndex(Sheet sheet)
		{
			if (sheet == null)
				return 0;

			for (var i = 0; i < sheets.Length; i++)
			{
				if (sheets[i] == sheet)
					return i;

				if (sheets[i] == null)
				{
					sheets[i] = sheet;
					return i;
				}
			}

			throw new InvalidDataException("Sheet overflow");
		}

		public void Update(MPos uv, Sprite sprite, PaletteReference palette, in float3 pos, float scale, float alpha, bool ignoreTint)
		{
			int2 samplers;
			if (sprite != null)
			{
				if (sprite.BlendMode != BlendMode)
					throw new InvalidDataException("Attempted to add sprite with a different blend mode");

				samplers = new int2(GetOrAddSheetIndex(sprite.Sheet), GetOrAddSheetIndex((sprite as SpriteWithSecondaryData)?.SecondarySheet));

				// PERF: Remove useless palette assignments for RGBA sprites
				// HACK: This is working around the limitation that palettes are defined on traits rather than on sequences,
				// and can be removed once this has been fixed
				if (sprite.Channel == TextureChannel.RGBA && !(palette?.HasColorShift ?? false))
					palette = null;
			}
			else
			{
				sprite = emptySprite;
				samplers = int2.Zero;
			}

			// The vertex buffer does not have geometry for cells outside the map
			if (!map.Tiles.Contains(uv))
				return;

			var offset = vertexRowStride * uv.V + 4 * uv.U;
			Util.FastCreateQuad(vertices, pos, sprite, samplers, palette?.TextureIndex ?? 0, offset, scale * sprite.Size, alpha * float3.Ones, alpha);
			palettes[uv.V * map.MapSize.X + uv.U] = palette;

			if (worldRenderer.TerrainLighting != null)
			{
				this.ignoreTint[offset] = ignoreTint;
				UpdateTint(uv);
			}

			dirtyRows.Add(uv.V);
		}

		public void Draw(Viewport viewport)
		{
			var cells = restrictToBounds ? viewport.VisibleCellsInsideBounds : viewport.AllVisibleCells;

			// Only draw the rows that are visible.
			var firstRow = cells.CandidateMapCoords.TopLeft.V.Clamp(0, map.MapSize.Y);
			var lastRow = (cells.CandidateMapCoords.BottomRight.V + 1).Clamp(firstRow, map.MapSize.Y);

			Game.Renderer.Flush();

			// Flush any visible changes to the GPU
			for (var row = firstRow; row <= lastRow; row++)
			{
				if (!dirtyRows.Remove(row))
					continue;

				var rowOffset = vertexRowStride * row;
				vertexBuffer.SetData(vertices, rowOffset, rowOffset, vertexRowStride);
			}

			Game.Renderer.WorldSpriteRenderer.DrawVertexBuffer(
				vertexBuffer, indexBufferWrapper.Buffer, indexRowStride * firstRow,
				indexRowStride * (lastRow - firstRow), sheets, BlendMode);

			Game.Renderer.Flush();
		}

		public void Dispose()
		{
			worldRenderer.PaletteInvalidated -= UpdatePaletteIndices;
			if (worldRenderer.TerrainLighting != null)
				worldRenderer.TerrainLighting.CellChanged -= UpdateTint;

			vertexBuffer.Dispose();

			lock (IndexBuffers)
				indexBufferWrapper.Dispose();
		}

		sealed class IndexBufferRc : IDisposable
		{
			public IIndexBuffer Buffer;
			int count;

			public IndexBufferRc(World world)
			{
				Buffer = Game.Renderer.Context.CreateIndexBuffer(Util.CreateQuadIndices(world.Map.MapSize.X * world.Map.MapSize.Y));
			}

			public void AddRef() { count++; }

			public void Dispose()
			{
				count--;
				if (count == 0)
					Buffer.Dispose();
			}
		}
	}
}
