using System;
using System.Buffers.Binary;
using System.Formats.Nrbf;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Pinta.Core;

/// <summary>
/// Imports Paint.NET (.pdn) images.
/// </summary>
public sealed class PdnFormat : IImageImporter
{
	// Indexed by Paint.NET's blend mode values. Modes without a Pinta equivalent fall back to Normal.
	private static readonly BlendMode[] blend_modes = [
		BlendMode.Normal,
		BlendMode.Multiply,
		BlendMode.Normal, // Additive
		BlendMode.ColorBurn,
		BlendMode.ColorDodge,
		BlendMode.Normal, // Reflect
		BlendMode.Normal, // Glow
		BlendMode.Overlay,
		BlendMode.Difference,
		BlendMode.Normal, // Negation
		BlendMode.Lighten,
		BlendMode.Darken,
		BlendMode.Screen,
		BlendMode.Xor,
	];

	// Class names of the blend ops used by older files, in the same order as blend_modes.
	private static readonly string[] blend_op_names = [
		"NormalBlendOp",
		"MultiplyBlendOp",
		"AdditiveBlendOp",
		"ColorBurnBlendOp",
		"ColorDodgeBlendOp",
		"ReflectBlendOp",
		"GlowBlendOp",
		"OverlayBlendOp",
		"DifferenceBlendOp",
		"NegationBlendOp",
		"LightenBlendOp",
		"DarkenBlendOp",
		"ScreenBlendOp",
		"XorBlendOp",
	];

	public Document Import (Gio.File file)
	{
		using GioStream stream = new (file.Read (cancellable: null));
		using BinaryReader reader = new (stream);

		reader.ReadBytes (4); // "PDN3"
		byte[] headerSize = reader.ReadBytes (3);
		reader.ReadBytes (headerSize[0] | headerSize[1] << 8 | headerSize[2] << 16); // XML header
		reader.ReadBytes (2); // 0x00 0x01

		ClassRecord pdnDocument = NrbfDecoder.DecodeClassRecord (stream, leaveOpen: true);

		Size imageSize = new (pdnDocument.GetInt32 ("width"), pdnDocument.GetInt32 ("height"));

		Document newDocument = new (
			PintaCore.Actions,
			PintaCore.Tools,
			PintaCore.Workspace,
			imageSize,
			file,
			"pdn");

		ClassRecord layerList = pdnDocument.GetClassRecord ("layers")!;
		int layerCount = layerList.GetInt32 ("ArrayList+_size");
		SerializationRecord?[] layerRecords = ((SZArrayRecord<SerializationRecord>) layerList.GetArrayRecord ("ArrayList+_items")!).GetArray ();

		for (int i = 0; i < layerCount; i++) {
			ClassRecord bitmapLayer = (ClassRecord) layerRecords[i]!;
			ClassRecord properties = bitmapLayer.GetClassRecord ("Layer+properties")!;
			ClassRecord surface = bitmapLayer.GetClassRecord ("surface")!;

			int stride = surface.GetInt32 ("stride");
			byte[] data = ReadPixelData (reader, (int) surface.GetClassRecord ("scan0")!.GetInt64 ("length64"));

			UserLayer layer = newDocument.Layers.CreateLayer (properties.GetString ("name"));
			newDocument.Layers.Insert (layer, i);

			layer.Hidden = !properties.GetBoolean ("visible");
			layer.Opacity = properties.GetByte ("opacity") / 255.0;
			layer.BlendMode = blend_modes[GetBlendModeIndex (bitmapLayer, properties)];

			Span<ColorBgra> pixels = layer.Surface.GetPixelData ();
			layer.Surface.Flush ();

			for (int y = 0; y < imageSize.Height; y++) {
				ReadOnlySpan<ColorBgra> row = MemoryMarshal.Cast<byte, ColorBgra> (data.AsSpan (y * stride, imageSize.Width * 4));
				for (int x = 0; x < imageSize.Width; x++)
					pixels[y * imageSize.Width + x] = row[x].ToPremultipliedAlpha ();
			}

			layer.Surface.MarkDirty ();
		}

		return newDocument;
	}

	private static int GetBlendModeIndex (ClassRecord bitmapLayer, ClassRecord properties)
		=> properties.HasMember ("blendMode")
			? properties.GetClassRecord ("blendMode")!.GetInt32 ("value__")
			: Array.IndexOf (blend_op_names, bitmapLayer.GetClassRecord ("properties")!.GetClassRecord ("blendOp")!.TypeName.FullName.Split ('+')[^1]);

	private static byte[] ReadPixelData (BinaryReader reader, int length)
	{
		byte[] data = new byte[length];

		bool compressed = reader.ReadByte () == 0;
		int chunkSize = ReadInt32BigEndian (reader);
		int chunkCount = (length + chunkSize - 1) / chunkSize;

		for (int i = 0; i < chunkCount; i++) {
			int chunkNumber = ReadInt32BigEndian (reader);
			int dataSize = ReadInt32BigEndian (reader);
			int offset = chunkNumber * chunkSize;

			MemoryStream raw = new (reader.ReadBytes (dataSize));
			using Stream chunk = compressed ? new GZipStream (raw, CompressionMode.Decompress) : raw;
			chunk.ReadExactly (data.AsSpan (offset, Math.Min (chunkSize, length - offset)));
		}

		return data;
	}

	private static int ReadInt32BigEndian (BinaryReader reader)
		=> (int) BinaryPrimitives.ReadUInt32BigEndian (reader.ReadBytes (4));
}
