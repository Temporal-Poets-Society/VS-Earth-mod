using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CraftProbe
{
    /// <summary>
    /// Earth test tool (EA-0068): the AgentBridge bench can press keys and click in the world but cannot move stacks between
    /// GUI slots. These commands put stacks into the calling player's own crafting grid (the inventory the 3x3 grid shows),
    /// let the grid find its recipe as it does after any slot change, and take the output through the grid's output slot
    /// (InventoryCraftingGrid.ActivateSlot, the call a click on the output slot makes), so the game's own code matches,
    /// consumes and crafts. Not for release.
    ///   /cprobe grid A B C D E F G H I   nine cells, row by row: "_" empty, or [item:|block:]domain:code[*count]
    ///   /cprobe take                     take the output (one craft) into the player's inventory
    ///   /cprobe clear                    empty the grid into the player's inventory
    ///   /cprobe barrel X Y Z STACK LITRES   place a barrel at X Y Z (absolute), STACK in its first slot and LITRES of
    ///                                       slaked lime in its liquid slot; reports what the barrel holds after
    /// </summary>
    public class CraftProbeSystem : ModSystem
    {
        ICoreServerAPI sapi;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            var p = api.ChatCommands.Parsers;
            var cmd = api.ChatCommands.Create("cprobe").WithDescription("Craft probe (Earth test tool)").RequiresPrivilege(Privilege.controlserver).RequiresPlayer();
            cmd.BeginSubCommand("grid").WithArgs(p.All("cells")).HandleWith(a => Grid((IServerPlayer)a.Caller.Player, ((string)a[0]).Split(' ', System.StringSplitOptions.RemoveEmptyEntries))).EndSubCommand();
            cmd.BeginSubCommand("take").HandleWith(a => Take((IServerPlayer)a.Caller.Player)).EndSubCommand();
            cmd.BeginSubCommand("clear").HandleWith(a => { Clear((IServerPlayer)a.Caller.Player); return TextCommandResult.Success("grid cleared"); }).EndSubCommand();
            cmd.BeginSubCommand("barrel").WithArgs(p.Int("x"), p.Int("y"), p.Int("z"), p.Word("stack"), p.Int("litres"))
                .HandleWith(a => Barrel((int)a[0], (int)a[1], (int)a[2], (string)a[3], (int)a[4])).EndSubCommand();
            cmd.BeginSubCommand("near").HandleWith(a =>
            {
                var plr = (IServerPlayer)a.Caller.Player;
                var sb = new StringBuilder("hotbar: ");
                var hot = plr.InventoryManager.GetHotbarInventory();
                for (int i = 0; i < hot.Count; i++) sb.Append(i).Append('=').Append(Describe(hot[i])).Append("; ");
                sb.Append("items within 8 blocks: ");
                foreach (var e in sapi.World.GetEntitiesAround(plr.Entity.Pos.XYZ, 8, 8, e => e is EntityItem))
                    sb.Append(((EntityItem)e).Itemstack?.StackSize + "x " + ((EntityItem)e).Itemstack?.Collectible.Code).Append(", ");
                return TextCommandResult.Success(sb.ToString());
            }).EndSubCommand();
            cmd.BeginSubCommand("empty").HandleWith(a =>
            {
                var hot = ((IServerPlayer)a.Caller.Player).InventoryManager.GetHotbarInventory();
                for (int i = 0; i < hot.Count; i++) { hot[i].Itemstack = null; hot[i].MarkDirty(); }
                return TextCommandResult.Success("hotbar emptied");
            }).EndSubCommand();
            cmd.BeginSubCommand("pit").WithArgs(p.Int("x"), p.Int("y"), p.Int("z")).HandleWith(a => Pit((int)a[0], (int)a[1], (int)a[2])).EndSubCommand();
        }

        /// <summary>A pit for a pit kiln at X Y Z (absolute): granite below and around, air in it and in the two cells above.</summary>
        TextCommandResult Pit(int x, int y, int z)
        {
            var ba = sapi.World.BlockAccessor;
            int granite = sapi.World.GetBlock(new AssetLocation("game:rock-granite")).Id;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    ba.SetBlock(granite, new BlockPos(x + dx, y - 1, z + dz, 0));
                    ba.SetBlock(dx == 0 && dz == 0 ? 0 : granite, new BlockPos(x + dx, y, z + dz, 0));
                    ba.SetBlock(0, new BlockPos(x + dx, y + 1, z + dz, 0));
                    ba.SetBlock(0, new BlockPos(x + dx, y + 2, z + dz, 0));
                }
            return TextCommandResult.Success("pit at =" + x + " =" + y + " =" + z + " (floor y " + (y - 1) + ")");
        }

        static IInventory GridOf(IServerPlayer plr) => plr.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);

        ItemStack Stack(string spec)
        {
            int n = 1, star = spec.IndexOf('*');
            if (star > 0) { n = int.Parse(spec.Substring(star + 1)); spec = spec.Substring(0, star); }
            bool block = spec.StartsWith("block:");
            if (spec.StartsWith("item:") || block) spec = spec.Substring(spec.IndexOf(':') + 1);
            var loc = new AssetLocation(spec);
            CollectibleObject c = block ? sapi.World.GetBlock(loc) : (CollectibleObject)sapi.World.GetItem(loc) ?? sapi.World.GetBlock(loc);
            return c == null ? null : new ItemStack(c, n);
        }

        static string Describe(ItemSlot s) => s?.Itemstack == null ? "_" : s.Itemstack.StackSize + "x " + s.Itemstack.Collectible.Code;

        TextCommandResult Grid(IServerPlayer plr, string[] cells)
        {
            if (cells.Length != 9) return TextCommandResult.Error("give nine cells");
            var grid = GridOf(plr);
            Clear(plr);
            for (int i = 0; i < 9; i++)
            {
                if (cells[i] == "_") continue;
                var st = Stack(cells[i]);
                if (st == null) return TextCommandResult.Error("no item or block " + cells[i]);
                grid[i].Itemstack = st;
                grid[i].MarkDirty();   // the grid looks for its recipe after a slot change, as after a player's move
            }
            var output = grid[9];
            string recipe = (grid as InventoryBase)?.GetType().GetField("MatchingRecipe")?.GetValue(grid) is GridRecipe r ? r.Name?.ToString() : "none";
            return TextCommandResult.Success("grid: " + string.Join(", ", Enumerable.Range(0, 9).Select(i => Describe(grid[i]))) + "; output " + Describe(output) + "; recipe " + recipe);
        }

        TextCommandResult Take(IServerPlayer plr)
        {
            var grid = GridOf(plr);
            var output = grid[9];
            if (output.Empty) return TextCommandResult.Error("the grid shows no output");
            var mouse = plr.InventoryManager.MouseItemSlot;
            if (!mouse.Empty) plr.InventoryManager.TryGiveItemstack(mouse.TakeOutWhole(), true);
            var op = new ItemStackMoveOperation(sapi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, output.StackSize) { ActingPlayer = plr };
            grid.ActivateSlot(9, mouse, ref op);
            string got = Describe(mouse);
            if (!mouse.Empty)
            {
                var st = mouse.TakeOutWhole();
                if (!plr.InventoryManager.TryGiveItemstack(st, true)) sapi.World.SpawnItemEntity(st, plr.Entity.Pos.XYZ);
                mouse.MarkDirty();
            }
            return TextCommandResult.Success("crafted " + got + "; grid now: " + string.Join(", ", Enumerable.Range(0, 9).Select(i => Describe(grid[i]))));
        }

        void Clear(IServerPlayer plr)
        {
            var grid = GridOf(plr);
            for (int i = 0; i < 9; i++)
            {
                if (grid[i].Empty) continue;
                var st = grid[i].TakeOutWhole();
                plr.InventoryManager.TryGiveItemstack(st, true);
                grid[i].MarkDirty();
            }
        }

        TextCommandResult Barrel(int x, int y, int z, string spec, int litres)
        {
            var pos = new BlockPos(x, y, z, 0);
            var block = sapi.World.GetBlock(new AssetLocation("game:barrel"));
            sapi.World.BlockAccessor.SetBlock(block.Id, pos);
            sapi.World.BlockAccessor.SpawnBlockEntity(block.EntityClass, pos);
            if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBarrel be) return TextCommandResult.Error("no barrel entity");
            var st = Stack(spec);
            if (st == null) return TextCommandResult.Error("no item or block " + spec);
            var lime = Stack("item:game:slakedlimeportion*" + (litres * 100));
            be.Inventory[0].Itemstack = st;
            be.Inventory[0].MarkDirty();
            be.Inventory[1].Itemstack = lime;
            be.Inventory[1].MarkDirty();   // the barrel looks for its recipe after a slot change and crafts at once (no seal time)
            be.MarkDirty(true);
            var sb = new StringBuilder("barrel at =" + x + " =" + y + " =" + z + " holds: ");
            for (int i = 0; i < be.Inventory.Count; i++) sb.Append(Describe(be.Inventory[i])).Append(i + 1 < be.Inventory.Count ? ", " : "");
            sb.Append("; recipe ").Append(be.CurrentRecipe?.Code ?? "none (or crafted)");
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
