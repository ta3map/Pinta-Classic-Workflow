using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Imports and exports Paint.NET (.pdn) images.
/// </summary>
public sealed class PdnFormat : IImageImporter, IImageExporter
{
	// Indexed by Paint.NET's blend mode values.
	private static readonly BlendMode[] blend_modes = [
		BlendMode.Normal,
		BlendMode.Multiply,
		BlendMode.Additive,
		BlendMode.ColorBurn,
		BlendMode.ColorDodge,
		BlendMode.Reflect,
		BlendMode.Glow,
		BlendMode.Overlay,
		BlendMode.Difference,
		BlendMode.Negation,
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

	private const string PDN_VERSION = "5.109.9343.2610";
	private const string DATA_LIBRARY = "PaintDotNet.Data, Version=" + PDN_VERSION + ", Culture=neutral, PublicKeyToken=null";
	private const string CORE_LIBRARY = "PaintDotNet.Core, Version=" + PDN_VERSION + ", Culture=neutral, PublicKeyToken=null";
	private const string METADATA_ITEM_TYPE = "System.Collections.Generic.KeyValuePair`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]";
	private const int CHUNK_SIZE = 0x40000;
	private const int THUMB_MAX_SIZE = 256;

	private const int DOCUMENT_ID = 1;
	private const int DATA_LIBRARY_ID = 2;
	private const int LAYER_LIST_ID = 3;
	private const int VERSION_ID = 4;
	private const int DOCUMENT_METADATA_ID = 5;
	private const int LAYER_METADATA_ID = 6;
	private const int LAYER_ITEMS_ID = 7;
	private const int CORE_LIBRARY_ID = 8;
	private const int LAYER_IDS_START = 10;
	private const int LAYER_IDS_STEP = 10;

	private enum BinaryType : byte { Primitive = 0, String = 1, SystemClass = 3, Class = 4, ObjectArray = 5 }
	private enum PrimitiveType : byte { Boolean = 1, Byte = 2, Int32 = 8, Int64 = 9 }

	private readonly record struct Member (string Name, BinaryType Type, PrimitiveType Primitive = 0, string ClassName = "", int LibraryId = 0);

	public void Export (Document document, Gio.File file, Gtk.Window parent)
	{
		IReadOnlyList<UserLayer> layers = document.Layers.UserLayers;
		Size size = document.ImageSize;

		using GioStream stream = new (file.Replace ());
		using BinaryWriter writer = new (stream);

		byte[] header = Encoding.UTF8.GetBytes (CreateXmlHeader (document));
		writer.Write (Encoding.ASCII.GetBytes ("PDN3"));
		writer.Write ((byte) header.Length);
		writer.Write ((byte) (header.Length >> 8));
		writer.Write ((byte) (header.Length >> 16));
		writer.Write (header);
		writer.Write ((byte) 0x00);
		writer.Write ((byte) 0x01);

		WriteDocument (writer, layers, size);

		foreach (UserLayer layer in layers)
			WritePixelData (writer, layer.Surface);
	}

	private static string CreateXmlHeader (Document document)
	{
		using ImageSurface flattened = document.GetFlattenedImage ();
		using GdkPixbuf.Pixbuf pixbuf = flattened.ToPixbuf ();
		double scale = Math.Min (1.0, (double) THUMB_MAX_SIZE / Math.Max (pixbuf.Width, pixbuf.Height));
		using GdkPixbuf.Pixbuf thumb = pixbuf.ScaleSimple (
			Math.Max (1, (int) (pixbuf.Width * scale)),
			Math.Max (1, (int) (pixbuf.Height * scale)),
			GdkPixbuf.InterpType.Bilinear)!;
		string png = Convert.ToBase64String (thumb.SaveToBuffer ("png"));

		return $"<pdnImage width=\"{document.ImageSize.Width}\" height=\"{document.ImageSize.Height}\" layers=\"{document.Layers.UserLayers.Count}\" savedWithVersion=\"{PDN_VERSION}\"><custom><thumb png=\"{png}\" /></custom></pdnImage>";
	}

	private static void WriteDocument (BinaryWriter writer, IReadOnlyList<UserLayer> layers, Size size)
	{
		// SerializedStreamHeader
		writer.Write ((byte) 0x00);
		writer.Write (DOCUMENT_ID);
		writer.Write (-1);
		writer.Write (1);
		writer.Write (0);

		WriteLibrary (writer, DATA_LIBRARY_ID, DATA_LIBRARY);

		WriteClassHeader (writer, DOCUMENT_ID, "PaintDotNet.Document", DATA_LIBRARY_ID, [
			new ("isDisposed", BinaryType.Primitive, PrimitiveType.Boolean),
			new ("layers", BinaryType.Class, ClassName: "PaintDotNet.LayerList", LibraryId: DATA_LIBRARY_ID),
			new ("width", BinaryType.Primitive, PrimitiveType.Int32),
			new ("height", BinaryType.Primitive, PrimitiveType.Int32),
			new ("savedWith", BinaryType.SystemClass, ClassName: "System.Version"),
			new ("userMetadataItems", BinaryType.SystemClass, ClassName: METADATA_ITEM_TYPE + "[]"),
		]);
		writer.Write (false);
		WriteReference (writer, LAYER_LIST_ID);
		writer.Write (size.Width);
		writer.Write (size.Height);
		WriteReference (writer, VERSION_ID);
		WriteReference (writer, DOCUMENT_METADATA_ID);

		WriteClassHeader (writer, LAYER_LIST_ID, "PaintDotNet.LayerList", DATA_LIBRARY_ID, [
			new ("parent", BinaryType.Class, ClassName: "PaintDotNet.Document", LibraryId: DATA_LIBRARY_ID),
			new ("ArrayList+_items", BinaryType.ObjectArray),
			new ("ArrayList+_size", BinaryType.Primitive, PrimitiveType.Int32),
			new ("ArrayList+_version", BinaryType.Primitive, PrimitiveType.Int32),
		]);
		WriteReference (writer, DOCUMENT_ID);
		WriteReference (writer, LAYER_ITEMS_ID);
		writer.Write (layers.Count);
		writer.Write (layers.Count);

		Version version = Version.Parse (PDN_VERSION);
		WriteSystemClassHeader (writer, VERSION_ID, "System.Version", [
			new ("_Major", BinaryType.Primitive, PrimitiveType.Int32),
			new ("_Minor", BinaryType.Primitive, PrimitiveType.Int32),
			new ("_Build", BinaryType.Primitive, PrimitiveType.Int32),
			new ("_Revision", BinaryType.Primitive, PrimitiveType.Int32),
		]);
		writer.Write (version.Major);
		writer.Write (version.Minor);
		writer.Write (version.Build);
		writer.Write (version.Revision);

		WriteEmptyMetadataArray (writer, DOCUMENT_METADATA_ID);
		WriteEmptyMetadataArray (writer, LAYER_METADATA_ID);

		// ArraySingleObject
		writer.Write ((byte) 0x10);
		writer.Write (LAYER_ITEMS_ID);
		writer.Write (layers.Count);
		for (int i = 0; i < layers.Count; i++)
			WriteReference (writer, LayerId (i));

		WriteLibrary (writer, CORE_LIBRARY_ID, CORE_LIBRARY);

		for (int i = 0; i < layers.Count; i++)
			WriteLayer (writer, layers[i], LayerId (i), i == 0, size);

		// MessageEnd
		writer.Write ((byte) 0x0B);
	}

	private static int LayerId (int index)
		=> LAYER_IDS_START + index * LAYER_IDS_STEP;

	private static void WriteLayer (BinaryWriter writer, UserLayer layer, int id, bool isBackground, Size size)
	{
		int propertiesId = id + 1;
		int blendOpId = id + 2;
		int surfaceId = id + 3;
		int memoryBlockId = id + 4;
		int layerPropertiesId = id + 5;
		int nameId = id + 6;
		int blendModeId = -(id + 7);

		int blendIndex = Math.Max (0, Array.IndexOf (blend_modes, layer.BlendMode));
		string blendOpClass = "PaintDotNet.UserBlendOps+" + blend_op_names[blendIndex];
		int stride = size.Width * 4;

		WriteClassHeader (writer, id, "PaintDotNet.BitmapLayer", DATA_LIBRARY_ID, [
			new ("properties", BinaryType.Class, ClassName: "PaintDotNet.BitmapLayer+BitmapLayerProperties", LibraryId: DATA_LIBRARY_ID),
			new ("surface", BinaryType.Class, ClassName: "PaintDotNet.Surface", LibraryId: CORE_LIBRARY_ID),
			new ("Layer+isDisposed", BinaryType.Primitive, PrimitiveType.Boolean),
			new ("Layer+width", BinaryType.Primitive, PrimitiveType.Int32),
			new ("Layer+height", BinaryType.Primitive, PrimitiveType.Int32),
			new ("Layer+properties", BinaryType.Class, ClassName: "PaintDotNet.Layer+LayerProperties", LibraryId: DATA_LIBRARY_ID),
		]);
		WriteReference (writer, propertiesId);
		WriteReference (writer, surfaceId);
		writer.Write (false);
		writer.Write (size.Width);
		writer.Write (size.Height);
		WriteReference (writer, layerPropertiesId);

		WriteClassHeader (writer, propertiesId, "PaintDotNet.BitmapLayer+BitmapLayerProperties", DATA_LIBRARY_ID, [
			new ("blendOp", BinaryType.Class, ClassName: blendOpClass, LibraryId: DATA_LIBRARY_ID),
		]);
		WriteReference (writer, blendOpId);

		WriteClassHeader (writer, blendOpId, blendOpClass, DATA_LIBRARY_ID, []);

		WriteClassHeader (writer, surfaceId, "PaintDotNet.Surface", CORE_LIBRARY_ID, [
			new ("width", BinaryType.Primitive, PrimitiveType.Int32),
			new ("height", BinaryType.Primitive, PrimitiveType.Int32),
			new ("stride", BinaryType.Primitive, PrimitiveType.Int32),
			new ("scan0", BinaryType.Class, ClassName: "PaintDotNet.MemoryBlock", LibraryId: CORE_LIBRARY_ID),
		]);
		writer.Write (size.Width);
		writer.Write (size.Height);
		writer.Write (stride);
		WriteReference (writer, memoryBlockId);

		WriteClassHeader (writer, memoryBlockId, "PaintDotNet.MemoryBlock", CORE_LIBRARY_ID, [
			new ("length64", BinaryType.Primitive, PrimitiveType.Int64),
			new ("hasParent", BinaryType.Primitive, PrimitiveType.Boolean),
			new ("deferred", BinaryType.Primitive, PrimitiveType.Boolean),
		]);
		writer.Write ((long) stride * size.Height);
		writer.Write (false);
		writer.Write (true);

		WriteClassHeader (writer, layerPropertiesId, "PaintDotNet.Layer+LayerProperties", DATA_LIBRARY_ID, [
			new ("name", BinaryType.String),
			new ("userMetadataItems", BinaryType.SystemClass, ClassName: METADATA_ITEM_TYPE + "[]"),
			new ("visible", BinaryType.Primitive, PrimitiveType.Boolean),
			new ("isBackground", BinaryType.Primitive, PrimitiveType.Boolean),
			new ("opacity", BinaryType.Primitive, PrimitiveType.Byte),
			new ("blendMode", BinaryType.Class, ClassName: "PaintDotNet.LayerBlendMode", LibraryId: DATA_LIBRARY_ID),
		]);
		// BinaryObjectString
		writer.Write ((byte) 0x06);
		writer.Write (nameId);
		writer.Write (layer.Name);
		WriteReference (writer, LAYER_METADATA_ID);
		writer.Write (!layer.Hidden);
		writer.Write (isBackground);
		writer.Write ((byte) Math.Round (layer.Opacity * 255));

		WriteClassHeader (writer, blendModeId, "PaintDotNet.LayerBlendMode", DATA_LIBRARY_ID, [
			new ("value__", BinaryType.Primitive, PrimitiveType.Int32),
		]);
		writer.Write (blendIndex);
	}

	private static void WriteLibrary (BinaryWriter writer, int id, string name)
	{
		writer.Write ((byte) 0x0C);
		writer.Write (id);
		writer.Write (name);
	}

	private static void WriteReference (BinaryWriter writer, int id)
	{
		writer.Write ((byte) 0x09);
		writer.Write (id);
	}

	private static void WriteEmptyMetadataArray (BinaryWriter writer, int id)
	{
		// BinaryArray: single dimension, zero length, of SystemClass elements
		writer.Write ((byte) 0x07);
		writer.Write (id);
		writer.Write ((byte) 0);
		writer.Write (1);
		writer.Write (0);
		writer.Write ((byte) BinaryType.SystemClass);
		writer.Write (METADATA_ITEM_TYPE);
	}

	private static void WriteClassHeader (BinaryWriter writer, int id, string name, int libraryId, Member[] members)
	{
		// ClassWithMembersAndTypes
		writer.Write ((byte) 0x05);
		WriteClassInfo (writer, id, name, members);
		writer.Write (libraryId);
	}

	private static void WriteSystemClassHeader (BinaryWriter writer, int id, string name, Member[] members)
	{
		// SystemClassWithMembersAndTypes
		writer.Write ((byte) 0x04);
		WriteClassInfo (writer, id, name, members);
	}

	private static void WriteClassInfo (BinaryWriter writer, int id, string name, Member[] members)
	{
		writer.Write (id);
		writer.Write (name);
		writer.Write (members.Length);

		foreach (Member member in members)
			writer.Write (member.Name);

		foreach (Member member in members)
			writer.Write ((byte) member.Type);

		foreach (Member member in members)
			WriteAdditionalInfo (writer, member);
	}

	private static void WriteAdditionalInfo (BinaryWriter writer, Member member)
	{
		switch (member.Type) {
			case BinaryType.Primitive:
				writer.Write ((byte) member.Primitive);
				break;
			case BinaryType.SystemClass:
				writer.Write (member.ClassName);
				break;
			case BinaryType.Class:
				writer.Write (member.ClassName);
				writer.Write (member.LibraryId);
				break;
		}
	}

	private static void WritePixelData (BinaryWriter writer, ImageSurface surface)
	{
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();
		byte[] data = new byte[pixels.Length * 4];
		Span<ColorBgra> straight = MemoryMarshal.Cast<byte, ColorBgra> (data.AsSpan ());
		for (int i = 0; i < pixels.Length; i++)
			straight[i] = pixels[i].ToStraightAlpha ();

		writer.Write ((byte) 0); // compressed
		WriteInt32BigEndian (writer, CHUNK_SIZE);

		for (int offset = 0, chunk = 0; offset < data.Length; offset += CHUNK_SIZE, chunk++) {
			using MemoryStream compressed = new ();
			using (GZipStream gzip = new (compressed, CompressionLevel.Fastest, leaveOpen: true))
				gzip.Write (data, offset, Math.Min (CHUNK_SIZE, data.Length - offset));

			WriteInt32BigEndian (writer, chunk);
			WriteInt32BigEndian (writer, (int) compressed.Length);
			writer.Write (compressed.GetBuffer (), 0, (int) compressed.Length);
		}
	}

	private static void WriteInt32BigEndian (BinaryWriter writer, int value)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian (bytes, value);
		writer.Write (bytes);
	}
}
