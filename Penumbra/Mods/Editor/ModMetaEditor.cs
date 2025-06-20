using System.Collections.Frozen;
using OtterGui.Classes;
using OtterGui.Services;
using Penumbra.Collections.Cache;
using Penumbra.Meta;
using Penumbra.Meta.Files;
using Penumbra.Meta.Manipulations;
using Penumbra.Mods.Groups;
using Penumbra.Mods.Manager.OptionEditor;
using Penumbra.Mods.SubMods;
using Penumbra.Services;
using static Penumbra.GameData.Files.ShpkFile;

namespace Penumbra.Mods.Editor;

public class ModMetaEditor(
    ModGroupEditor groupEditor,
    MetaFileManager metaFileManager) : MetaDictionary, IService
{
    public sealed class OtherOptionData : HashSet<string>
    {
        public int TotalCount;

        public void Add(string name, int count)
        {
            if (count > 0)
                Add(name);
            TotalCount += count;
        }

        public new void Clear()
        {
            TotalCount = 0;
            base.Clear();
        }
    }

    public readonly FrozenDictionary<MetaManipulationType, OtherOptionData> OtherData =
        Enum.GetValues<MetaManipulationType>().ToFrozenDictionary(t => t, _ => new OtherOptionData());

    public bool Changes { get; set; }

    public new void Clear()
    {
        Changes = Count > 0;
        base.Clear();
    }

    public void Load(Mod mod, IModDataContainer currentOption)
    {
        foreach (var type in Enum.GetValues<MetaManipulationType>())
            OtherData[type].Clear();

        foreach (var option in mod.AllDataContainers)
        {
            if (option == currentOption)
                continue;

            var name = option.GetFullName();
            OtherData[MetaManipulationType.Imc].Add(name, option.Manipulations.GetCount(MetaManipulationType.Imc));
            OtherData[MetaManipulationType.Eqp].Add(name, option.Manipulations.GetCount(MetaManipulationType.Eqp));
            OtherData[MetaManipulationType.Eqdp].Add(name, option.Manipulations.GetCount(MetaManipulationType.Eqdp));
            OtherData[MetaManipulationType.Gmp].Add(name, option.Manipulations.GetCount(MetaManipulationType.Gmp));
            OtherData[MetaManipulationType.Est].Add(name, option.Manipulations.GetCount(MetaManipulationType.Est));
            OtherData[MetaManipulationType.Rsp].Add(name, option.Manipulations.GetCount(MetaManipulationType.Rsp));
            OtherData[MetaManipulationType.Atch].Add(name, option.Manipulations.GetCount(MetaManipulationType.Atch));
            OtherData[MetaManipulationType.Shp].Add(name, option.Manipulations.GetCount(MetaManipulationType.Shp));
            OtherData[MetaManipulationType.Atr].Add(name, option.Manipulations.GetCount(MetaManipulationType.Atr));
            OtherData[MetaManipulationType.GlobalEqp].Add(name, option.Manipulations.GetCount(MetaManipulationType.GlobalEqp));
        }

        Clear();
        UnionWith(currentOption.Manipulations);
        Changes = false;
    }

    public static bool DeleteDefaultValues(Mod mod, MetaFileManager metaFileManager, SaveService? saveService, bool deleteAll = false)
    {
        if (deleteAll)
        {
            var changes = false;
            foreach (var container in mod.AllDataContainers)
            {
                if (!DeleteDefaultValues(metaFileManager, container.Manipulations))
                    continue;

                saveService?.ImmediateSaveSync(new ModSaveGroup(container, metaFileManager.Config.ReplaceNonAsciiOnImport));
                changes = true;
            }

            return changes;
        }

        var defaultEntries = new MultiDictionary<IMetaIdentifier, IModDataContainer>();
        var actualEntries  = new HashSet<IMetaIdentifier>();
        if (!FilterDefaultValues(mod.AllDataContainers, metaFileManager, defaultEntries, actualEntries))
            return false;

        var            groups     = new HashSet<IModGroup>();
        DefaultSubMod? defaultMod = null;
        foreach (var (defaultIdentifier, containers) in defaultEntries.Grouped)
        {
            if (!deleteAll && actualEntries.Contains(defaultIdentifier))
                continue;

            foreach (var container in containers)
            {
                if (!container.Manipulations.Remove(defaultIdentifier))
                    continue;

                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {defaultIdentifier}.");
                if (container.Group is { } group)
                    groups.Add(group);
                else if (container is DefaultSubMod d)
                    defaultMod = d;
            }
        }

        if (saveService is not null)
        {
            if (defaultMod is not null)
                saveService.ImmediateSaveSync(new ModSaveGroup(defaultMod, metaFileManager.Config.ReplaceNonAsciiOnImport));
            foreach (var group in groups)
                saveService.ImmediateSaveSync(new ModSaveGroup(group, metaFileManager.Config.ReplaceNonAsciiOnImport));
        }

        return defaultMod is not null || groups.Count > 0;
    }

    public void DeleteDefaultValues()
        => Changes = DeleteDefaultValues(metaFileManager, this);

    public void Apply(IModDataContainer container)
    {
        if (!Changes)
            return;

        groupEditor.SetManipulations(container, this);
        Changes = false;
    }

    private static bool FilterDefaultValues(IEnumerable<IModDataContainer> containers, MetaFileManager metaFileManager,
        MultiDictionary<IMetaIdentifier, IModDataContainer> defaultEntries, HashSet<IMetaIdentifier> actualEntries)
    {
        if (!metaFileManager.CharacterUtility.Ready)
        {
            Penumbra.Log.Warning("Trying to filter default meta values before CharacterUtility was ready, skipped.");
            return false;
        }

        foreach (var container in containers)
        {
            foreach (var (key, value) in container.Manipulations.Imc)
            {
                var defaultEntry = ImcChecker.GetDefaultEntry(key, false);
                if (defaultEntry.Entry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Eqp)
            {
                var defaultEntry = new EqpEntryInternal(ExpandedEqpFile.GetDefault(metaFileManager, key.SetId), key.Slot);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Eqdp)
            {
                var defaultEntry = new EqdpEntryInternal(ExpandedEqdpFile.GetDefault(metaFileManager, key), key.Slot);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Est)
            {
                var defaultEntry = EstFile.GetDefault(metaFileManager, key);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Gmp)
            {
                var defaultEntry = ExpandedGmpFile.GetDefault(metaFileManager, key);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Rsp)
            {
                var defaultEntry = CmpFile.GetDefault(metaFileManager, key.SubRace, key.Attribute);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }

            foreach (var (key, value) in container.Manipulations.Atch)
            {
                var defaultEntry = AtchCache.GetDefault(metaFileManager, key);
                if (defaultEntry.Equals(value))
                    defaultEntries.TryAdd(key, container);
                else
                    actualEntries.Add(key);
            }
        }

        return true;
    }

    private static bool DeleteDefaultValues(MetaFileManager metaFileManager, MetaDictionary dict)
    {
        if (!metaFileManager.CharacterUtility.Ready)
        {
            Penumbra.Log.Warning("Trying to delete default meta values before CharacterUtility was ready, skipped.");
            return false;
        }

        var clone = dict.Clone();
        dict.ClearForDefault();

        var count = 0;
        foreach (var value in clone.GlobalEqp)
            dict.TryAdd(value);

        foreach (var (key, value) in clone.Imc)
        {
            var defaultEntry = ImcChecker.GetDefaultEntry(key, false);
            if (!defaultEntry.Entry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Eqp)
        {
            var defaultEntry = new EqpEntryInternal(ExpandedEqpFile.GetDefault(metaFileManager, key.SetId), key.Slot);
            if (!defaultEntry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Eqdp)
        {
            var defaultEntry = new EqdpEntryInternal(ExpandedEqdpFile.GetDefault(metaFileManager, key), key.Slot);
            if (!defaultEntry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Est)
        {
            var defaultEntry = EstFile.GetDefault(metaFileManager, key);
            if (!defaultEntry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Gmp)
        {
            var defaultEntry = ExpandedGmpFile.GetDefault(metaFileManager, key);
            if (!defaultEntry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Rsp)
        {
            var defaultEntry = CmpFile.GetDefault(metaFileManager, key.SubRace, key.Attribute);
            if (!defaultEntry.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        foreach (var (key, value) in clone.Atch)
        {
            var defaultEntry = AtchCache.GetDefault(metaFileManager, key);
            if (!defaultEntry.HasValue)
                continue;

            if (!defaultEntry.Value.Equals(value))
            {
                dict.TryAdd(key, value);
            }
            else
            {
                Penumbra.Log.Verbose($"Deleted default-valued meta-entry {key}.");
                ++count;
            }
        }

        if (count == 0)
            return false;

        Penumbra.Log.Debug($"Deleted {count} default-valued meta-entries from a mod option.");
        return true;
    }
}
