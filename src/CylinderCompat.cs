using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Reads Cylinder World's world geometry (its SeamParams) by reflection. Cylinder World is a required dependency
    /// (0.11.11 or newer, decision 0023), but Earth Map doesn't compile against its assembly: every member is looked up by
    /// name and missing ones are reported.
    /// </summary>
    public sealed class CylinderCompat
    {
        readonly object p;
        readonly Type t;

        CylinderCompat(object p)
        {
            this.p = p;
            t = p.GetType();
        }

        /// <summary>Cylinder World's params for this world, or null if it isn't installed or not active here.</summary>
        public static CylinderCompat TryGet(ICoreAPI api)
        {
            var core = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "CylinderWorld.CylinderWorldCore");
            var prop = core?.GetType().GetProperty("Params", BindingFlags.Public | BindingFlags.Instance);
            var p = prop?.GetValue(core);
            return p == null ? null : new CylinderCompat(p);
        }

        /// <summary>Configuration preflight before Cylinder World captures its new-world geometry at WorldReady.
        /// Its Params do not exist yet. Reject known-incompatible settings without changing vanilla's sea level.</summary>
        public static bool HasEarthConfiguration(ICoreAPI api)
        {
            var core = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "CylinderWorld.CylinderWorldCore");
            var config = core?.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.Instance)?.GetValue(core);
            if (config == null) return false;
            bool Flag(string name)
            {
                var type = config.GetType();
                object value = type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(config)
                    ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(config);
                return value is true;
            }
            return Flag("Enabled") && Flag("PolarWalls") && Flag("PolarClimate");
        }

        /// <summary>Makes (x, z) Cylinder World's spawn too, so it moves vanilla's story chain there (Cylinder World 0.11.11+,
        /// new worlds before world generation). False when it can't.</summary>
        public static bool SetSpawn(ICoreAPI api, int x, int z)
        {
            var core = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "CylinderWorld.CylinderWorldCore");
            var m = core?.GetType().GetMethod("SetSpawn", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int), typeof(int) }, null);
            return m != null && m.Invoke(core, new object[] { x, z }) is true;
        }

        public double Get(string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return Convert.ToDouble(f.GetValue(p));
            var pr = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pr != null) return Convert.ToDouble(pr.GetValue(p));
            throw new MissingMemberException("CylinderWorld.SeamParams", name);
        }

        public bool GetBool(string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return (bool)f.GetValue(p);
            var pr = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pr != null) return (bool)pr.GetValue(p);
            throw new MissingMemberException("CylinderWorld.SeamParams", name);
        }

        public bool TrySet(string field, object value)
        {
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(p, Convert.ChangeType(value, f.FieldType));
            return true;
        }

        public double JumpInset => Get("JumpInset");
        public double Circumference => Get("Circumference");
        public double EquatorZ => Get("EquatorZ");
        public double PoleDistance => Get("PoleDistance");
        public double SeamHalfWidth => Get("SeamHalfWidth");
        public int SeabedY => (int)Get("SeabedY");
        public int SpawnX => (int)Get("SpawnX");
        public int SpawnZ => (int)Get("SpawnZ");
        public bool HasWalls => Get("WallDepth") > 0;
        public bool PolarClimate => GetBool("PolarClimate");
        public int Version => (int)Get("Version");
    }
}
