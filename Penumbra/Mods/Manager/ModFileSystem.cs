using Dalamud.Interface.ImGuiNotification;
using OtterGui.Classes;
using OtterGui.Filesystem;
using OtterGui.Services;
using Penumbra.Communication;
using Penumbra.Services;

namespace Penumbra.Mods.Manager;

public sealed class ModFileSystem : FileSystem<Mod>, IDisposable, ISavable, IService
{
    private readonly ModManager          _modManager;
    private readonly CommunicatorService _communicator;
    private readonly SaveService         _saveService;
    private readonly Configuration       _config;

    // Create a new ModFileSystem from the currently loaded mods and the current sort order file.
    public ModFileSystem(ModManager modManager, CommunicatorService communicator, SaveService saveService, Configuration config)
    {
        _modManager   = modManager;
        _communicator = communicator;
        _saveService  = saveService;
        _config       = config;
        Reload();
        Changed += OnChange;
        _communicator.ModDiscoveryFinished.Subscribe(Reload, ModDiscoveryFinished.Priority.ModFileSystem);
        _communicator.ModDataChanged.Subscribe(OnModDataChange, ModDataChanged.Priority.ModFileSystem);
        _communicator.ModPathChanged.Subscribe(OnModPathChange, ModPathChanged.Priority.ModFileSystem);
    }

    public void Dispose()
    {
        _communicator.ModPathChanged.Unsubscribe(OnModPathChange);
        _communicator.ModDiscoveryFinished.Unsubscribe(Reload);
        _communicator.ModDataChanged.Unsubscribe(OnModDataChange);
    }

    public struct ImportDate : ISortMode<Mod>
    {
        public string Name
            => "Import Date (Older First)";

        public string Description
            => "In each folder, sort all subfolders lexicographically, then sort all leaves using their import date.";

        public IEnumerable<IPath> GetChildren(Folder f)
            => f.GetSubFolders().Cast<IPath>().Concat(f.GetLeaves().OrderBy(l => l.Value.ImportDate));
    }

    public struct InverseImportDate : ISortMode<Mod>
    {
        public string Name
            => "Import Date (Newer First)";

        public string Description
            => "In each folder, sort all subfolders lexicographically, then sort all leaves using their inverse import date.";

        public IEnumerable<IPath> GetChildren(Folder f)
            => f.GetSubFolders().Cast<IPath>().Concat(f.GetLeaves().OrderByDescending(l => l.Value.ImportDate));
    }

    // Reload the whole filesystem from currently loaded mods and the current sort order file.
    // Used on construction and on mod rediscoveries.
    private void Reload()
    {
        var jObj = BackupService.GetJObjectForFile(_saveService.FileNames, _saveService.FileNames.FilesystemFile);
        if (Load(jObj, _modManager, ModToIdentifier, ModToName))
            _saveService.ImmediateSave(this);

        Penumbra.Log.Debug("Reloaded mod filesystem.");
    }

    // Save the filesystem on every filesystem change except full reloading.
    private void OnChange(FileSystemChangeType type, IPath _1, IPath? _2, IPath? _3)
    {
        if (type != FileSystemChangeType.Reload)
            _saveService.DelaySave(this);
    }

    // Update sort order when defaulted mod names change.
    private void OnModDataChange(ModDataChangeType type, Mod mod, string? oldName)
    {
        if (!type.HasFlag(ModDataChangeType.Name) || oldName == null || !TryGetValue(mod, out var leaf))
            return;

        var old = oldName.FixName();
        if (old == leaf.Name || leaf.Name.IsDuplicateName(out var baseName, out _) && baseName == old)
            RenameWithDuplicates(leaf, mod.Name.Text);
    }

    // Update the filesystem if a mod has been added or removed.
    // Save it, if the mod directory has been moved, since this will change the save format.
    private void OnModPathChange(ModPathChangeType type, Mod mod, DirectoryInfo? oldPath, DirectoryInfo? newPath)
    {
        switch (type)
        {
            case ModPathChangeType.Added:
                var parent = Root;
                if (_config.DefaultImportFolder.Length != 0)
                    try
                    {
                        parent = FindOrCreateAllFolders(_config.DefaultImportFolder);
                    }
                    catch (Exception e)
                    {
                        Penumbra.Messager.NotificationMessage(e,
                            $"Could not move newly imported mod {mod.Name} to default import folder {_config.DefaultImportFolder}.",
                            NotificationType.Warning);
                    }

                CreateDuplicateLeaf(parent, mod.Name.Text, mod);
                break;
            case ModPathChangeType.Deleted:
                if (TryGetValue(mod, out var leaf))
                    Delete(leaf);

                break;
            case ModPathChangeType.Moved:
                _saveService.DelaySave(this);
                break;
            case ModPathChangeType.Reloaded:
                // Nothing
                break;
        }
    }

    // Used for saving and loading.
    private static string ModToIdentifier(Mod mod)
        => mod.ModPath.Name;

    private static string ModToName(Mod mod)
        => mod.Name.Text.FixName();

    // Return whether a mod has a custom path or is just a numbered default path.
    public static bool ModHasDefaultPath(Mod mod, string fullPath)
    {
        var regex = new Regex($@"^{Regex.Escape(ModToName(mod))}( \(\d+\))?$");
        return regex.IsMatch(fullPath);
    }

    private static (string, bool) SaveMod(Mod mod, string fullPath)
        // Only save pairs with non-default paths.
        => ModHasDefaultPath(mod, fullPath)
            ? (string.Empty, false)
            : (ModToIdentifier(mod), true);

    public string ToFilename(FilenameService fileNames)
        => fileNames.FilesystemFile;

    public void Save(StreamWriter writer)
        => SaveToFile(writer, SaveMod, true);

    public string TypeName
        => "Mod File System";

    public string LogName(string _)
        => "to file";
}
