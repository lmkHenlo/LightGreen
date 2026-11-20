using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace LightGreen.Presets
{
    /// <summary>
    /// The choices for Filters.Filter: None and every filter LightGreen has loaded, read afresh each time. BepInEx writes
    /// them into the config file as "# Acceptable values:", which r2modman and in-game config editors show as a dropdown.
    /// Unlike BepInEx's own list it accepts any name, so a filter added while playing, or typed by hand, isn't reset to
    /// None; a name that isn't a filter shows no filter and is reported in the log.
    /// </summary>
    internal sealed class FilterChoices : AcceptableValueList<string>
    {
        internal FilterChoices() : base(Current()) { }

        public override string[] AcceptableValues => Current();

        public override bool IsValid(object value) => value is string;

        public override object Clamp(object value) =>
            value is string name && name.Trim().Length > 0 ? name : FilterStore.NoFilter;

        public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", Current());

        /// <summary>None, then the loaded filters in F11 order (the built-in ones until the filters file is read).</summary>
        internal static string[] Current()
        {
            var names = new List<string> { FilterStore.NoFilter };
            if (FilterStore.Names.Count > 0) names.AddRange(FilterStore.Names);
            else names.AddRange(DefaultFilters.Create().filters.Select(f => f.name));
            return names.ToArray();
        }
    }
}
