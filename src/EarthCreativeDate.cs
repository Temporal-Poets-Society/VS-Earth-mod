using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Setting the date for testing: /earth date and the Month and Day dropdowns under the creative panel's time slider.
    /// Vanilla's calendar has 12 months of DaysPerMonth game days (9 by default); a real date maps onto the game day that
    /// covers the same part of its month (15 July -> July day 5 of 9). Time only moves forward, through vanilla's own
    /// /time add, like /time setmonth: going back would leave crops, animals, food and weather with last-update times in
    /// the future. A date that has passed this year means next year's.
    /// </summary>
    public static class EarthDate
    {
        static readonly string[] MonthCodes = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        static readonly int[] RealMonthDays = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        public static int RealDays(int month0) => RealMonthDays[month0];

        /// <summary>Game day of the month (0-based) covering real day <paramref name="realDay"/> (1-based).</summary>
        public static int GameDay0(int realDay, int month0, int daysPerMonth)
        {
            int len = RealMonthDays[month0];
            realDay = Math.Clamp(realDay, 1, len);
            return Math.Min(daysPerMonth - 1, (realDay - 1) * daysPerMonth / len);
        }

        /// <summary>First real day (1-based) that falls on game day <paramref name="gameDay0"/> of the month.</summary>
        public static int FirstRealDay(int gameDay0, int month0, int daysPerMonth)
        {
            int len = RealMonthDays[month0];
            return Math.Min(len, (gameDay0 * len + daysPerMonth - 1) / daysPerMonth + 1);
        }

        /// <summary>Last real day (1-based) that falls on game day <paramref name="gameDay0"/>.</summary>
        public static int LastRealDay(int gameDay0, int month0, int daysPerMonth) =>
            gameDay0 >= daysPerMonth - 1 ? RealMonthDays[month0] : Math.Max(FirstRealDay(gameDay0, month0, daysPerMonth), FirstRealDay(gameDay0 + 1, month0, daysPerMonth) - 1);

        public static string MonthName(int month0) => Lang.Get("month-" + (EnumMonth)(month0 + 1));

        public static string RealRange(int gameDay0, int month0, int daysPerMonth)
        {
            int a = FirstRealDay(gameDay0, month0, daysPerMonth), b = LastRealDay(gameDay0, month0, daysPerMonth);
            return a == b ? a.ToString(CultureInfo.InvariantCulture) : a + "–" + b;
        }

        /// <summary>Whole game days since the calendar's zero, and that day's year, month and day of the month (0-based).</summary>
        public static void Now(IGameCalendar cal, out long day, out int year, out int month0, out int gameDay0)
        {
            day = (long)Math.Floor(cal.TotalDays);
            int perYear = cal.DaysPerYear, perMonth = cal.DaysPerMonth;
            year = (int)Math.Floor((double)day / perYear);
            int dayOfYear = (int)(day - (long)year * perYear);
            month0 = Math.Min(11, dayOfYear / perMonth);
            gameDay0 = dayOfYear - month0 * perMonth;
        }

        public static string Describe(IGameCalendar cal)
        {
            Now(cal, out _, out int year, out int m, out int g);
            int perMonth = cal.DaysPerMonth;
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} (game day {2} of {3}), year {4}, {5:00}:{6:00}",
                RealRange(g, m, perMonth), MonthName(m), g + 1, perMonth, year, cal.FullHourOfDay, (int)((cal.HourOfDay % 1) * 60));
        }

        /// <summary>Month from jan..dec, a full English month name or 1..12; -1 if not a month.</summary>
        public static int ParseMonth(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            s = s.Trim().ToLowerInvariant();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n >= 1 && n <= 12 ? n - 1 : -1;
            if (s.Length >= 3) for (int i = 0; i < 12; i++) if (s.StartsWith(MonthCodes[i])) return i;
            return -1;
        }

        public static void Register(ICoreServerAPI api, IChatCommand earth)
        {
            var parsers = api.ChatCommands.Parsers;
            earth.BeginSubCommand("date")
                .WithDescription("Show the date, or jump forward to a real date, e.g. /earth date 15 jul (year optional). Months have " +
                    "DaysPerMonth game days, so a real date lands on the game day covering it. Time only runs forward: a date that has " +
                    "passed means next year's; the time of day is kept. Uses vanilla's /time add.")
                .RequiresPrivilege(Privilege.time)
                .WithArgs(parsers.OptionalInt("day", 0), parsers.OptionalWord("month"), parsers.OptionalInt("year", -1))
                .HandleWith(args => Jump(api, args))
            .EndSubCommand();
        }

        static TextCommandResult Jump(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            var cal = api.World.Calendar;
            if (args.Parsers[0].IsMissing) return TextCommandResult.Success("It is " + Describe(cal) + ".");
            int day = (int)args[0];
            int m = ParseMonth(args[1] as string);
            if (m < 0) return TextCommandResult.Error("Give a day and a month, e.g. /earth date 15 jul (month as jan..dec or 1..12).");
            if (day < 1 || day > RealDays(m)) return TextCommandResult.Error(string.Format("{0} has days 1 to {1}.", MonthName(m), RealDays(m)));
            bool yearGiven = !args.Parsers[2].IsMissing;
            int perYear = cal.DaysPerYear, perMonth = cal.DaysPerMonth;
            Now(cal, out long today, out int year, out _, out _);
            int g = GameDay0(day, m, perMonth);
            int targetYear = yearGiven ? (int)args[2] : year;
            long target = (long)targetYear * perYear + (long)m * perMonth + g;
            if (target < today)
            {
                if (yearGiven) return TextCommandResult.Error("Time only moves forward here; it is now " + Describe(cal) + ".");
                target += perYear;
            }
            if (target == today) return TextCommandResult.Success("It is already that day: " + Describe(cal) + ".");
            double hours = (target - today) * (double)cal.HoursPerDay;
            TextCommandResult added = null;
            api.ChatCommands.ExecuteUnparsed("/time add " + hours.ToString("0.###", CultureInfo.InvariantCulture) + " hours",
                new TextCommandCallingArgs { Caller = args.Caller, LanguageCode = args.LanguageCode }, r => added = r);
            if (added != null && added.Status == EnumCommandStatus.Error) return added;
            return TextCommandResult.Success(string.Format(CultureInfo.InvariantCulture, "Moved {0} game days forward. It is now {1}.",
                target - today, Describe(cal)));
        }
    }

    /// <summary>
    /// Month and Day dropdowns in the creative (WorldEdit) settings panel, under the time slider. The rows are added
    /// when the panel is built (a JSON patch would not last: WorldEdit reloads its dialog files from disk the first time
    /// it opens). The panel's value handlers are private and hard-coded, so two prefixes answer for our two rows and
    /// send /earth date; every other row is left to vanilla.
    /// </summary>
    public class EarthCreativeDate : ModSystem
    {
        const string MonthCode = "earthdatemonth", DayCode = "earthdateday", HarmonyId = "earth.creativedate";
        static ICoreClientAPI capi;
        /// <summary>WorldEdit's client handler's settings dialog (bound by PatchVanilla's CreativeDateTable).</summary>
        internal static FieldInfo SettingsDialogField;
        Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            harmony = new Harmony(HarmonyId);
            // all three patches and the dialog field or none (one line naming the member a game update changed): without
            // them the panel has no date rows, and /earth date still sets the date
            PatchVanilla.For(api, Mod, harmony).Apply(PatchVanilla.CreativeDateTable());
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            SettingsDialogField = null;
            capi = null;
            base.Dispose();
        }

        /// <summary>Before the creative settings panel is built: put the Month and Day rows under the time slider.</summary>
        internal static void AddRows(JsonDialogSettings settings)
        {
            if (settings?.Code != "worldedit-settings" || settings.Rows == null) return;
            if (settings.Rows.Any(r => r?.Elements?.Any(e => e?.Code == MonthCode) == true)) return;
            int at = Array.FindIndex(settings.Rows, r => r?.Elements?.Any(e => e?.Code == "timeofday") == true);
            var like = at < 0 ? null : settings.Rows[at];
            at = at < 0 ? settings.Rows.Length : at + 1;
            DialogRow Row(string code, string label) => new DialogRow(new DialogElement
            {
                Code = code, Label = label, Tooltip = "earth:creative-date-tooltip", Width = 350,
                Type = EnumDialogElementType.DynamicSelect, Mode = EnumDialogElementMode.DropDown
            }) { BottomPadding = like?.BottomPadding ?? 10f };
            var rows = settings.Rows.ToList();
            rows.InsertRange(at, new[] { Row(MonthCode, "earth:creative-date-month"), Row(DayCode, "earth:creative-date-day") });
            settings.Rows = rows.ToArray();
        }

        internal static bool OnGet(string elementCode, ref string __result)
        {
            if (elementCode != MonthCode && elementCode != DayCode) return true;
            var cal = capi?.World?.Calendar;
            if (cal == null) { __result = "\n\n"; return false; }
            EarthDate.Now(cal, out _, out _, out int m, out int g);
            int perMonth = cal.DaysPerMonth;
            if (elementCode == MonthCode)
            {
                var values = Enumerable.Range(0, 12).Select(i => (i + 1).ToString(CultureInfo.InvariantCulture));
                var names = Enumerable.Range(0, 12).Select(EarthDate.MonthName);
                __result = string.Join("||", values) + "\n" + string.Join("||", names) + "\n" + (m + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // one entry per game day, valued by the first real day it covers; named by the real days it covers
                var days = Enumerable.Range(0, perMonth);
                __result = string.Join("||", days.Select(d => EarthDate.FirstRealDay(d, m, perMonth).ToString(CultureInfo.InvariantCulture))) + "\n" +
                    string.Join("||", days.Select(d => EarthDate.RealRange(d, m, perMonth))) + "\n" +
                    EarthDate.FirstRealDay(g, m, perMonth).ToString(CultureInfo.InvariantCulture);
            }
            return false;
        }

        internal static bool OnSet(object __instance, string elementCode, string newValue)
        {
            if (elementCode != MonthCode && elementCode != DayCode) return true;
            var cal = capi?.World?.Calendar;
            if (cal == null || string.IsNullOrEmpty(newValue)) return false;
            EarthDate.Now(cal, out _, out _, out int m, out int g);
            int perMonth = cal.DaysPerMonth;
            string cmd;
            if (elementCode == MonthCode)
            {
                if (!int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int month) || month < 1 || month > 12) return false;
                // keep the same place in the month
                cmd = "/earth date " + EarthDate.FirstRealDay(g, month - 1, perMonth) + " " + month;
            }
            else
            {
                if (!int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) return false;
                cmd = "/earth date " + day + " " + (m + 1);
            }
            capi.SendChatMessage(cmd);
            // the server's calendar packet follows the command; then show the new month's days and the new date
            var dialog = SettingsDialogField?.GetValue(__instance) as GuiJsonDialog;
            if (dialog != null) capi.Event.RegisterCallback(_ => { if (dialog.IsOpened()) dialog.ReloadValues(); }, 1500);
            return false;
        }
    }
}
