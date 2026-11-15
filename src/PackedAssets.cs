using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Earth's large config JSON travels brotli-packed in the release zip (<c>name.json.br</c>, written by the build's
    /// PackJsonAssets step) to stay under the Mod DB's 40 MB; the repository keeps the plain JSON. Before any other system
    /// reads an asset, this unpacks each packed one and adds it under its plain name (<c>name.json</c>) with the same bytes
    /// the repository holds, so every reader, Earth's or another mod's, finds the asset it always did. A plain asset of the
    /// same name, if one is there (a development build), wins.
    /// </summary>
    public sealed class PackedAssets : ModSystem
    {
        public const string Suffix = ".br";

        // before every other system's AssetsLoaded (EarthFauna reads its rules there)
        public override double ExecuteOrder() => 0;

        public override void AssetsLoaded(ICoreAPI api)
        {
            var packed = api.Assets.AllAssets.Where(p => p.Key.Domain == "earth" && p.Key.Path.EndsWith(".json" + Suffix, StringComparison.Ordinal)).ToList();
            int added = 0;
            foreach (var (location, asset) in packed)
            {
                var plain = new AssetLocation(location.Domain, location.Path.Substring(0, location.Path.Length - Suffix.Length));
                if (api.Assets.Exists(plain)) continue;
                try
                {
                    var bytes = api.Assets.TryGet(location)?.Data;
                    if (bytes == null) continue;
                    api.Assets.Add(plain, new Unpacked(plain, (byte[])bytes.Clone(), asset.Origin));
                    added++;
                }
                catch (Exception e) { Mod.Logger.Error("Earth: could not unpack {0}: {1}", location, e.Message); }
            }
            if (added > 0) Mod.Logger.Notification("Earth: unpacked {0} packed config assets.", added);
        }

        public static byte[] Unpack(byte[] packed)
        {
            // (ER-26 A7: one exact copy at the end, not a buffer doubling six times; fauna-species unpacks 45 kB to 2.8 MB)
            return EarthMap.Core.EarthBinary.UnbrotliAll(packed, 0, "packed asset");
        }

        /// <summary>
        /// An unpacked asset: vanilla's Asset is not in the API, so its text and JSON handling are repeated here. It keeps
        /// the packed bytes and unpacks on demand: after start-up the server drops the bytes of every unpatched asset
        /// (AssetManager.UnloadUnpatchedAssets sets Data to null), and the mod's origin cannot load this one again (it only
        /// knows name.json.br), so a later reader (/wgen regen, /earth where) would get nothing. Setting Data to null drops
        /// the unpacked copy; the next read unpacks again. A JSON patch's result (IsPatched) is kept as set.
        /// </summary>
        internal sealed class Unpacked : IAsset
        {
            readonly byte[] packed;
            byte[] data;
            public Unpacked(AssetLocation location, byte[] packedBytes, IAssetOrigin origin) { Location = location; packed = packedBytes; Origin = origin; }
            public string Name => Location.GetName();
            public AssetLocation Location { get; }
            public IAssetOrigin Origin { get; set; }
            public byte[] Data
            {
                get => data ??= Unpack(packed);
                set => data = value;
            }
            public bool IsPatched { get; set; }
            public bool IsLoaded() => true;
            public string ToText()
            {
                var d = Data;
                // like vanilla's Asset: UTF-8, without a byte-order mark
                return d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? Encoding.UTF8.GetString(d, 3, d.Length - 3) : Encoding.UTF8.GetString(d);
            }
            public T ToObject<T>(JsonSerializerSettings settings = null)
            {
                try { return JsonUtil.ToObject<T>(ToText(), Location.Domain, settings); }
                catch (Exception ex) { throw new JsonReaderException("Failed deserializing " + Name + ": " + ex.Message); }
            }
            public BitmapRef ToBitmap(ICoreClientAPI capi) => capi.Render.BitmapCreateFromPng(Data);
            public override string ToString() => Location.ToString();
        }
    }
}
