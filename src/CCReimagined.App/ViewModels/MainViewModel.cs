using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CCReimagined.App.Models;
using CCReimagined.App.Services;
using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using CCReimagined.Core.Profiles;
using CCReimagined.Core.Providers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCReimagined.App.ViewModels;

/// <summary>
/// Drives the whole flow the original tool's main form did: pick a server, pick a database,
/// pick a table or view, confirm the key and the list parameters, generate.
///
/// The difference is that nothing here knows which engine is on the other end. Every call
/// goes through <see cref="IDatabaseProvider"/>, and the emitted code's engine is a separate
/// choice, so a SQL Server schema can be generated against Npgsql if that is what you want.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly CSharpDataClassGenerator _generator = new();
    private CancellationTokenSource? _inFlight;

    /// <summary>
    /// Supplied by the window once it exists. Null in the XAML previewer and in tests, where
    /// the copy and save commands simply do nothing.
    /// </summary>
    public IShellServices? Shell { get; set; }

    /// <summary>
    /// The database call currently in flight, if any. Selecting a database or a relation
    /// starts work that the UI does not await; exposing it lets a test wait for the same
    /// work to finish instead of polling.
    /// </summary>
    public Task PendingWork { get; private set; } = Task.CompletedTask;

    /// <param name="store">
    /// Where saved connections live. Tests pass a store over a temporary file; the app lets it
    /// default to the per-user config directory.
    /// </param>
    public MainViewModel(ProfileStore? store = null)
    {
        _store = store ?? new ProfileStore();

        Providers = new ObservableCollection<IDatabaseProvider>(ProviderRegistry.All);
        CodegenProfiles = new ObservableCollection<ICodegenProfile>(ProviderRegistry.All.Select(p => p.CodegenProfile));
        EditorThemes = new ObservableCollection<EditorThemeOption>(EditorThemeOption.All);

        SelectedProvider = Providers[0];
        SelectedCodegenProfile = SelectedProvider.CodegenProfile;
        SelectedEditorTheme = EditorThemeOption.Default;

        LoadProfiles();
        _loaded = true;
    }

    private readonly ProfileStore _store;

    /// <summary>Set while a saved profile is being applied, so the field setters do not fight back.</summary>
    private bool _applyingProfile;

    /// <summary>
    /// False until the constructor has finished reading the profile file. Without this, the
    /// initial property assignments fire their change handlers and persist an empty book over
    /// the user's saved profiles before they are ever loaded.
    /// </summary>
    private bool _loaded;

    // ------------------------------------------------------------------ connection

    public ObservableCollection<IDatabaseProvider> Providers { get; }

    [ObservableProperty]
    public partial IDatabaseProvider SelectedProvider { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; } = "localhost";

    [ObservableProperty]
    public partial string Port { get; set; } = "";

    /// <summary>Initial catalog, or the file path when the provider's database is a file.</summary>
    [ObservableProperty]
    public partial string DatabaseOrPath { get; set; } = "";

    [ObservableProperty]
    public partial bool UseIntegratedSecurity { get; set; } = true;

    [ObservableProperty]
    public partial string UserName { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool TrustServerCertificate { get; set; } = true;

    /// <summary>The modern form of the old tool's manual-DSN box.</summary>
    [ObservableProperty]
    public partial bool UseRawConnectionString { get; set; }

    [ObservableProperty]
    public partial string RawConnectionString { get; set; } = "";

    /// <summary>The connection string actually in use, shown so it can be copied or checked.</summary>
    [ObservableProperty]
    public partial string EffectiveConnectionString { get; set; } = "";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Pick a provider and connect.";

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    // ------------------------------------------------------------------ saved profiles

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    public partial ConnectionProfile? SelectedProfile { get; set; }

    /// <summary>
    /// The name Save writes under. Editing it and pressing Save creates a new profile, which is
    /// how "save as" works without a modal dialog.
    /// </summary>
    [ObservableProperty]
    public partial string ProfileName { get; set; } = "";

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Where the profiles are stored, shown on the Connection tab so it is findable.</summary>
    public string ProfileStorePath => _store.Path;

    // ------------------------------------------------------------------ editor

    public ObservableCollection<EditorThemeOption> EditorThemes { get; }

    [ObservableProperty]
    public partial EditorThemeOption SelectedEditorTheme { get; set; }

    // ------------------------------------------------------------------ schema browsing

    public ObservableCollection<string> Databases { get; } = [];

    [ObservableProperty]
    public partial string? SelectedDatabase { get; set; }

    public ObservableCollection<RelationRow> Relations { get; } = [];

    /// <summary>Every relation discovered; <see cref="Relations"/> is this filtered.</summary>
    private readonly List<RelationRow> _allRelations = [];

    [ObservableProperty]
    public partial string RelationFilter { get; set; } = "";

    [ObservableProperty]
    public partial RelationRow? SelectedRelation { get; set; }

    public ObservableCollection<ColumnRow> Columns { get; } = [];

    [ObservableProperty]
    public partial TableSchema? CurrentSchema { get; set; }

    [ObservableProperty]
    public partial string SchemaSummary { get; set; } = "";

    // ------------------------------------------------------------------ generation options

    public ObservableCollection<ICodegenProfile> CodegenProfiles { get; }

    [ObservableProperty]
    public partial ICodegenProfile SelectedCodegenProfile { get; set; }

    [ObservableProperty]
    public partial string TargetNamespace { get; set; } = "Generated.Data";

    [ObservableProperty]
    public partial string ClassName { get; set; } = "";

    [ObservableProperty]
    public partial bool GenerateSyncWrappers { get; set; } = true;

    [ObservableProperty]
    public partial bool ImplementInpc { get; set; }

    [ObservableProperty]
    public partial bool TruncateOverlongStrings { get; set; } = true;

    [ObservableProperty]
    public partial bool TreatEmptyStringAsNull { get; set; }

    [ObservableProperty]
    public partial bool GenerateRecExists { get; set; } = true;

    [ObservableProperty]
    public partial bool GenerateReadAsDataTable { get; set; } = true;

    [ObservableProperty]
    public partial bool GenerateGetAll { get; set; } = true;

    [ObservableProperty]
    public partial bool SealedClass { get; set; } = true;

    /// <summary>
    /// Null follows the database, which is the default. The UI exposes it as a tri-state so a
    /// view the engine misjudges can be overruled either way.
    /// </summary>
    [ObservableProperty]
    public partial bool? MutatingMethodsForViews { get; set; }

    [ObservableProperty]
    public partial string ListMaxRows { get; set; } = "0";

    // ------------------------------------------------------------------ output

    [ObservableProperty]
    public partial string GeneratedCode { get; set; } = "";

    [ObservableProperty]
    public partial string GeneratedFileName { get; set; } = "";

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    public partial bool HasOutput { get; set; }

    [ObservableProperty]
    public partial bool HasWarnings { get; set; }

    /// <summary>
    /// Which right-hand tab is showing. Generating switches to the output, so the result is in
    /// front of the user instead of waiting behind a tab they have to remember to click.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    private const int SchemaTabIndex = 0;
    private const int OutputTabIndex = 2;

    // ------------------------------------------------------------------ derived UI state

    public bool NeedsHost => SelectedProvider.Capabilities.NeedsHost && !UseRawConnectionString;

    public bool DatabaseIsFilePath => SelectedProvider.Capabilities.DatabaseIsFilePath;

    public bool SupportsIntegratedAuth => SelectedProvider.Capabilities.SupportsIntegratedAuth;

    public bool SupportsDatabaseEnumeration => SelectedProvider.Capabilities.SupportsDatabaseEnumeration;

    public bool NeedsCredentials => !UseRawConnectionString
                                    && (!SupportsIntegratedAuth || !UseIntegratedSecurity);

    public string DatabaseLabel => DatabaseIsFilePath ? "Database file" : "Database";

    /// <summary>"Databases" for most engines, "Schemas" for Oracle.</summary>
    public string DatabaseListLabel => SelectedProvider.Capabilities.DatabaseListLabel;

    /// <summary>
    /// True everywhere except macOS, where the menu belongs in the system bar at the top of the
    /// screen rather than inside the window. The window carries both and shows the right one.
    /// </summary>
    public static bool ShowsWindowMenu => !OperatingSystem.IsMacOS();

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var provider = SelectedProvider;
        var settings = BuildSettings();

        await RunAsync("Connecting…", async ct =>
        {
            var connectionString = provider.BuildConnectionString(settings);
            EffectiveConnectionString = connectionString;

            var probe = await provider.TestConnectionAsync(connectionString, ct);

            if (!probe.Success)
            {
                IsConnected = false;
                ClearSchema();
                Databases.Clear();
                Fail(probe.Error ?? "Connection failed.");
                return;
            }

            IsConnected = true;
            Databases.Clear();
            ClearSchema();

            // Only a connection that actually worked is worth reopening on next launch.
            if (SelectedProfile is { } profile)
                PersistProfiles(profile.Name);

            if (provider.Capabilities.SupportsDatabaseEnumeration)
            {
                foreach (var db in await provider.ListDatabasesAsync(connectionString, ct))
                    Databases.Add(db);

                var current = settings.Database;
                var match = Databases.FirstOrDefault(d =>
                    string.Equals(d, current, StringComparison.OrdinalIgnoreCase));

                if (provider.Capabilities.DatabaseListKind == DatabaseListKind.Schema)
                {
                    // These are schemas inside the connection, not catalogs to reconnect to.
                    // The relations are already loaded and schema-qualified; selecting one
                    // filters them. Nothing is auto-selected, because the connected service is
                    // not one of these entries and picking an arbitrary one would be a guess.
                    await LoadRelationsAsync(connectionString, ct);
                    Report($"{probe.ServerVersion} — {Databases.Count} schema(s), {_allRelations.Count} relation(s).");
                }
                else
                {
                    // Land on the catalog the connection string already names, when it is listed.
                    SelectedDatabase = match ?? Databases.FirstOrDefault();
                    Report($"{probe.ServerVersion} — {Databases.Count} database(s).");
                }
            }
            else
            {
                // SQLite and friends: one implicit catalog, so go straight to its relations.
                await LoadRelationsAsync(connectionString, ct);
                Report($"{probe.ServerVersion} — {_allRelations.Count} table(s) and view(s).");
            }
        });
    }

    [RelayCommand]
    private void Disconnect()
    {
        _inFlight?.Cancel();
        IsConnected = false;
        Databases.Clear();
        SelectedDatabase = null;
        ClearSchema();
        EffectiveConnectionString = "";
        Report("Disconnected.");
    }

    [RelayCommand]
    private void Generate()
    {
        var schema = CurrentSchema;

        if (schema is null)
        {
            Fail("Select a table or view first.");
            return;
        }

        var key = Columns.FirstOrDefault(c => c.IsKey)?.Column;

        var request = new GenerationRequest
        {
            Schema = schema with { KeyColumn = key ?? schema.KeyColumn },
            Profile = SelectedCodegenProfile,
            Namespace = string.IsNullOrWhiteSpace(TargetNamespace) ? "Generated.Data" : TargetNamespace.Trim(),
            ClassName = string.IsNullOrWhiteSpace(ClassName) ? null : ClassName.Trim(),
            KeyColumnName = key?.Name,
            ListParameterColumns = Columns.Where(c => c.IsListParameter).Select(c => c.Name).ToList(),
            SourceDescription = DescribeSource(),
            Options = new GenerationOptions
            {
                GenerateSyncWrappers = GenerateSyncWrappers,
                ImplementINotifyPropertyChanged = ImplementInpc,
                TruncateOverlongStrings = TruncateOverlongStrings,
                TreatEmptyStringAsNull = TreatEmptyStringAsNull,
                GenerateRecExists = GenerateRecExists,
                GenerateReadAsDataTable = GenerateReadAsDataTable,
                GenerateGetAll = GenerateGetAll,
                SealedClass = SealedClass,
                MutatingMethodsForViews = MutatingMethodsForViews,
                ListMaxRows = int.TryParse(ListMaxRows, out var rows) && rows > 0 ? rows : 0,
            },
        };

        var artifact = _generator.Generate(request);

        GeneratedCode = artifact.Content;
        GeneratedFileName = artifact.FileName;
        HasOutput = true;

        Warnings.Clear();
        foreach (var warning in artifact.Warnings)
            Warnings.Add(warning);

        HasWarnings = Warnings.Count > 0;

        SelectedTabIndex = OutputTabIndex;

        var lineCount = artifact.Content.Count(ch => ch == '\n') + 1;
        Report($"Generated {artifact.FileName} — {lineCount} lines for {SelectedCodegenProfile.DisplayName}." +
               (artifact.Warnings.Count > 0 ? $" {artifact.Warnings.Count} warning(s)." : ""));
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Shell is null || !HasOutput)
            return;

        await Shell.CopyToClipboardAsync(GeneratedCode);
        Report($"{GeneratedFileName} copied to the clipboard.");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Shell is null || !HasOutput)
            return;

        try
        {
            var path = await Shell.SaveTextFileAsync(GeneratedFileName, GeneratedCode);

            if (path is not null)
                Report($"Saved to {path}.");
        }
        catch (Exception ex)
        {
            Fail($"Could not save: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowseDatabaseFileAsync()
    {
        if (Shell is null)
            return;

        // Open the picker where the current path points, so correcting a typo does not mean
        // navigating from the top again.
        var current = FilePath.Resolve(DatabaseOrPath);
        var startIn = Path.GetDirectoryName(current) is { Length: > 0 } dir && Directory.Exists(dir)
            ? dir
            : null;

        var chosen = await Shell.PickDatabaseFileAsync(startIn);

        if (chosen is null)
            return;

        DatabaseOrPath = chosen;
        Report($"Selected {chosen}.");
    }

    [RelayCommand]
    private void Exit() => Shell?.Shutdown();

    [RelayCommand]
    private void SaveProfile()
    {
        var name = ProfileName.Trim();

        if (name.Length == 0)
        {
            Fail("Give the profile a name before saving it.");
            return;
        }

        var profile = CaptureProfile(name);

        var existing = Profiles.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            Profiles[Profiles.IndexOf(existing)] = profile;
        else
            Profiles.Add(profile);

        SelectedProfile = profile;
        PersistProfiles(name);

        Report(existing is null
            ? $"Saved profile '{name}'."
            : $"Updated profile '{name}'.");
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { } profile)
        {
            Fail("Select a profile to delete.");
            return;
        }

        Profiles.Remove(profile);
        SelectedProfile = null;
        ProfileName = "";
        OnPropertyChanged(nameof(HasProfiles));
        PersistProfiles(null);

        Report($"Deleted profile '{profile.Name}'.");
    }

    /// <summary>Snapshots the connection fields. The password is never captured.</summary>
    private ConnectionProfile CaptureProfile(string name) => new ConnectionProfile
    {
        Name = name,
        ProviderId = SelectedProvider.Id,
        Host = Host.Trim(),
        Port = int.TryParse(Port, out var port) && port > 0 ? port : null,
        Database = string.IsNullOrWhiteSpace(DatabaseOrPath) ? null : DatabaseOrPath.Trim(),
        UseIntegratedSecurity = UseIntegratedSecurity,
        UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
        TrustServerCertificate = TrustServerCertificate,
        UseRawConnectionString = UseRawConnectionString,
        RawConnectionString = string.IsNullOrWhiteSpace(RawConnectionString) ? null : RawConnectionString.Trim(),
        TargetNamespace = TargetNamespace,
        CodegenProfileId = SelectedCodegenProfile.Id,
        LastUsedUtc = DateTimeOffset.UtcNow,
    }.ScrubSecrets();

    private void LoadProfiles()
    {
        var book = _store.Load();

        Profiles.Clear();
        foreach (var profile in book.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(profile);

        OnPropertyChanged(nameof(HasProfiles));

        SelectedEditorTheme = EditorThemeOption.ByName(book.EditorThemeName);

        // Reopening on the connection the user left off with is the whole point of remembering.
        var last = Profiles.FirstOrDefault(p =>
            p.Name.Equals(book.LastUsedProfileName, StringComparison.OrdinalIgnoreCase));

        if (last is not null)
        {
            SelectedProfile = last;
            Report($"Restored profile '{last.Name}'. Passwords are never saved, so re-enter one if the server needs it.");
        }
    }

    private void PersistProfiles(string? lastUsedName)
    {
        if (!_loaded)
            return;

        try
        {
            _store.Save(new ProfileBook
            {
                Profiles = [.. Profiles],
                LastUsedProfileName = lastUsedName ?? SelectedProfile?.Name,
                EditorThemeName = SelectedEditorTheme.Name,
            });
        }
        catch (Exception ex)
        {
            Fail($"Could not save profiles: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ reactions to selection

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value is null)
            return;

        ApplyProfile(value);
    }

    partial void OnSelectedEditorThemeChanged(EditorThemeOption value)
    {
        // The theme is a preference, not part of any profile, so it is saved on its own.
        PersistProfiles(null);
    }

    /// <summary>
    /// Pushes a saved profile into the connection fields. The password is left alone — it was
    /// never saved, so whatever the user has typed this session stays put.
    /// </summary>
    private void ApplyProfile(ConnectionProfile profile)
    {
        _applyingProfile = true;

        try
        {
            if (ProviderRegistry.TryById(profile.ProviderId) is { } provider)
                SelectedProvider = provider;

            Host = profile.Host;
            Port = profile.Port?.ToString() ?? "";
            DatabaseOrPath = profile.Database ?? "";
            UseIntegratedSecurity = profile.UseIntegratedSecurity;
            UserName = profile.UserName ?? "";
            TrustServerCertificate = profile.TrustServerCertificate;
            UseRawConnectionString = profile.UseRawConnectionString;
            RawConnectionString = profile.RawConnectionString ?? "";
            ProfileName = profile.Name;

            if (!string.IsNullOrWhiteSpace(profile.TargetNamespace))
                TargetNamespace = profile.TargetNamespace!;

            if (profile.CodegenProfileId is not null &&
                CodegenProfiles.FirstOrDefault(c => c.Id == profile.CodegenProfileId) is { } codegen)
            {
                SelectedCodegenProfile = codegen;
            }
        }
        finally
        {
            _applyingProfile = false;
        }

        RaiseConnectionShapeChanged();
    }

    partial void OnSelectedProviderChanged(IDatabaseProvider value)
    {
        // Applying a saved profile sets the provider first and the rest straight after, so the
        // defaults below must not overwrite what the profile is about to supply.
        if (_applyingProfile)
        {
            IsConnected = false;
            Databases.Clear();
            ClearSchema();
            RaiseConnectionShapeChanged();
            return;
        }

        // Default the emitted code to the engine being browsed; the user can still retarget.
        SelectedCodegenProfile = value.CodegenProfile;

        var defaultPort = value.Capabilities.DefaultPort;
        Port = defaultPort > 0 ? defaultPort.ToString() : "";

        if (!value.Capabilities.SupportsIntegratedAuth)
            UseIntegratedSecurity = false;

        IsConnected = false;
        Databases.Clear();
        ClearSchema();
        RaiseConnectionShapeChanged();
        Report($"{value.DisplayName} selected.");
    }

    partial void OnUseRawConnectionStringChanged(bool value) => RaiseConnectionShapeChanged();

    partial void OnUseIntegratedSecurityChanged(bool value) => OnPropertyChanged(nameof(NeedsCredentials));

    partial void OnSelectedDatabaseChanged(string? value)
    {
        if (value is null || !IsConnected)
            return;

        var provider = SelectedProvider;

        if (provider.Capabilities.DatabaseListKind == DatabaseListKind.Schema)
        {
            // A schema is part of an object's name, not somewhere to connect. Writing one into
            // the connection string breaks it — on Oracle with an error that blames the syntax.
            ApplyRelationFilter();
            Report($"{value}: {Relations.Count} of {_allRelations.Count} relation(s).");
            return;
        }

        var connectionString = provider.WithDatabase(EffectiveConnectionString, value);
        EffectiveConnectionString = connectionString;

        _ = RunAsync($"Reading {value}…", async ct =>
        {
            await LoadRelationsAsync(connectionString, ct);
            Report($"{value}: {_allRelations.Count} table(s) and view(s).");
        });
    }

    partial void OnRelationFilterChanged(string value) => ApplyRelationFilter();

    partial void OnSelectedRelationChanged(RelationRow? value)
    {
        if (value is null)
            return;

        var provider = SelectedProvider;
        var connectionString = EffectiveConnectionString;

        _ = RunAsync($"Reading columns of {value.Display}…", async ct =>
        {
            var schema = await provider.GetTableSchemaAsync(connectionString, value.Relation, ct);

            CurrentSchema = schema;
            SelectedTabIndex = SchemaTabIndex;
            Columns.Clear();

            foreach (var column in schema.Columns)
            {
                var row = new ColumnRow(column)
                {
                    IsKey = schema.KeyColumn is not null && column.Name == schema.KeyColumn.Name,
                };

                // Selecting one key must clear the others, so the grid stays a radio group.
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ColumnRow.IsKey) && row.IsKey)
                    {
                        foreach (var other in Columns.Where(c => !ReferenceEquals(c, row)))
                            other.IsKey = false;
                    }
                };

                Columns.Add(row);
            }

            ClassName = Naming.ToClassName(value.Relation.QualifiedName);

            var unmapped = schema.Columns.Count(c => c.ClrType == ClrTypeKind.Unknown);

            var viewNote = schema.Table.Kind == RelationKind.View
                ? $" — view, {schema.Mutability.Describe()}"
                : "";

            SchemaSummary =
                $"{schema.Columns.Count} column(s), " +
                $"key: {schema.KeyColumn?.Name ?? "none found"}" +
                (schema.HasGeneratedKey ? " (database-generated)" : " (not auto-numbered)") +
                (unmapped > 0 ? $", {unmapped} unmapped type(s)" : "") +
                viewNote;

            // The old tool popped a modal warning here and carried on regardless; saying it
            // once in the summary line is enough, and generation still works either way.
            if (!schema.HasGeneratedKey)
            {
                Report($"{value.Display}: no auto-numbering key column — " +
                       $"'{schema.KeyColumn?.Name}' was assumed. Pick another in the Key column if that is wrong.");
            }
            else
            {
                Report($"{value.Display}: {schema.Columns.Count} column(s).");
            }
        });
    }

    // ------------------------------------------------------------------ internals

    private ConnectionSettings BuildSettings() => new()
    {
        Host = Host.Trim(),
        Port = int.TryParse(Port, out var port) && port > 0 ? port : null,
        Database = string.IsNullOrWhiteSpace(DatabaseOrPath) ? null : DatabaseOrPath.Trim(),
        AuthMode = UseIntegratedSecurity && SupportsIntegratedAuth ? AuthMode.Integrated : AuthMode.UserPassword,
        UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
        Password = Password,
        TrustServerCertificate = TrustServerCertificate,
        RawConnectionString = UseRawConnectionString && !string.IsNullOrWhiteSpace(RawConnectionString)
            ? RawConnectionString.Trim()
            : null,
    };

    private async Task LoadRelationsAsync(string connectionString, CancellationToken ct)
    {
        var relations = await SelectedProvider.ListRelationsAsync(connectionString, ct);

        _allRelations.Clear();
        _allRelations.AddRange(relations.Select(r => new RelationRow(r)));

        ClearSchemaSelection();
        ApplyRelationFilter();
    }

    private void ApplyRelationFilter()
    {
        var filter = RelationFilter.Trim();
        var previous = SelectedRelation?.Display;

        Relations.Clear();

        // For a schema-listing engine the selected entry narrows the list; for the others the
        // relations already belong to the connected catalog, so only the text box applies.
        var schema = SelectedProvider.Capabilities.DatabaseListKind == DatabaseListKind.Schema
            ? SelectedDatabase
            : null;

        foreach (var row in _allRelations)
        {
            if (schema is not null &&
                !string.Equals(row.Relation.Schema, schema, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filter.Length == 0 || row.Display.Contains(filter, StringComparison.OrdinalIgnoreCase))
                Relations.Add(row);
        }

        // Keep the current selection when it survives the filter, rather than snapping away.
        if (previous is not null)
            SelectedRelation = Relations.FirstOrDefault(r => r.Display == previous);
    }

    private void ClearSchema()
    {
        _allRelations.Clear();
        Relations.Clear();
        ClearSchemaSelection();
    }

    private void ClearSchemaSelection()
    {
        SelectedRelation = null;
        CurrentSchema = null;
        Columns.Clear();
        SchemaSummary = "";
        Warnings.Clear();
        HasWarnings = false;
        GeneratedCode = "";
        GeneratedFileName = "";
        HasOutput = false;
    }

    private void RaiseConnectionShapeChanged()
    {
        OnPropertyChanged(nameof(NeedsHost));
        OnPropertyChanged(nameof(DatabaseIsFilePath));
        OnPropertyChanged(nameof(SupportsIntegratedAuth));
        OnPropertyChanged(nameof(SupportsDatabaseEnumeration));
        OnPropertyChanged(nameof(NeedsCredentials));
        OnPropertyChanged(nameof(DatabaseLabel));
        OnPropertyChanged(nameof(DatabaseListLabel));
    }

    private string DescribeSource()
    {
        var provider = SelectedProvider;

        if (provider.Capabilities.DatabaseIsFilePath)
            return $"{provider.DisplayName} — {DatabaseOrPath}";

        var database = SelectedDatabase ?? DatabaseOrPath;
        return $"{provider.DisplayName} — {Host}{(string.IsNullOrEmpty(database) ? "" : "/" + database)}";
    }

    /// <summary>
    /// Runs one unit of database work off the UI thread, reporting failure in the status bar
    /// rather than throwing. A new call cancels the previous one, so clicking through a long
    /// table list does not queue up a backlog of catalog queries.
    /// </summary>
    private Task RunAsync(string message, Func<CancellationToken, Task> work)
    {
        var task = RunCoreAsync(message, work);
        PendingWork = task;
        return task;
    }

    private async Task RunCoreAsync(string message, Func<CancellationToken, Task> work)
    {
        _inFlight?.Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;

        IsBusy = true;
        Report(message);

        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; the newer one owns the status line now.
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                IsBusy = false;
                _inFlight = null;
            }

            cts.Dispose();
        }
    }

    private void Report(string message)
    {
        StatusMessage = message;
        StatusIsError = false;
    }

    private void Fail(string message)
    {
        StatusMessage = message;
        StatusIsError = true;
    }
}
